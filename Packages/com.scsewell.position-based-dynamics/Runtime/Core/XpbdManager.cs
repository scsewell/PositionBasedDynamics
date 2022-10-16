using System;
using System.Collections.Generic;

using Unity.Collections;
using Unity.Mathematics;

using UnityEngine;
using UnityEngine.PlayerLoop;
using UnityEngine.Rendering;

namespace Scsewell.PositionBasedDynamics
{
    /// <summary>
    /// An enum that defines the options for progressing the particle simulation.
    /// </summary>
    public enum XpbdUpdateMode
    {
        /// <summary>
        /// The simulation is automatically progressed every frame using a fixed timestep.
        /// </summary>
        Automatic,
        
        /// <summary>
        /// The simulation is progressed by calling <see cref="XpbdManager.Simulate"/> on demand.
        /// </summary>
        Manual,
    }
    
    /// <summary>
    /// A class that manager the simulation of XPBD particles.
    /// </summary>
    public class XpbdManager
    {
        [Flags]
        internal enum DirtyFlags
        {
            None = 0,
            UpdateBuffers = 1 << 0,
            ResetParticles = 1 << 1,
            Gravity = 1 << 2,
            All = ~0,
        }
        
        class ClothState
        {
            public DirtyFlags dirtyFlags;
            public bool isCreated;
            public int particleBaseIndex;
            public int particleCount;
        }
        
        // shader resources
        static bool s_resourcesInitialized;
        static XpbdResources s_resources;
        static ComputeShader s_resetParticlesShader;
        static ComputeShader s_integrateShader;
        static ComputeShader s_solveDistanceConstraintsShader;
        static ComputeShader s_updateVelocitiesShader;
        static KernelInfo s_resetParticlesKernel;
        static KernelInfo s_copyParticlesKernel;
        static KernelInfo s_integrateKernel;
        static KernelInfo s_solveDistanceConstraintsKernel;
        static KernelInfo s_updateVelocitiesKernel;
        
        // command buffers
        static CommandBuffer s_subStepCmdBuffer;
        
        // constant buffers
        static NativeArray<SimulationPropertyBuffer> s_simulationProperties;
        static ComputeBuffer s_simulationConstantBuffer;
        
        // buffers filled on the CPU with static data
        static ComputeBuffer s_particleGroupIndicesBuffer;
        static ComputeBuffer s_inverseMassesBuffer;
        static ComputeBuffer s_restPositionsBuffer;
        static ComputeBuffer s_distanceConstraintsBuffer;
        
        // buffers filled on the CPU with dynamic data
        static ComputeBuffer s_gravityBuffer;
        
        // buffers filled on the GPU
        static ComputeBuffer s_positionsBuffer;
        static ComputeBuffer s_prevPositionsBuffer;
        static ComputeBuffer s_velocitiesBuffer;
        
        // managed state
        static int s_particleCount;
        static int s_distanceConstraintCount;

        static bool s_enabled;
        static DirtyFlags s_dirtyFlags;
        static float s_deltaTimeRemainder;
        static XpbdUpdateMode s_updateMode;
        static float s_subStepsPerSecond;
        static int s_maxSubStepsPerFrame;

        static readonly List<ICloth> s_cloths = new List<ICloth>();
        static readonly List<ClothState> s_clothStates = new List<ClothState>();
        
        /// <summary>
        /// Is the simulator enabled.
        /// </summary>
        public static bool Enabled => s_enabled;

        /// <summary>
        /// Controls how the particle simulation is progressed.
        /// </summary>
        public static XpbdUpdateMode UpdateMode
        {
            get => s_updateMode;
            set => s_updateMode = value;
        }

        /// <summary>
        /// The target number of simulation substeps to run every second.
        /// </summary>
        /// <remarks>
        /// Raising this value will increase the simulation accuracy at the cost of performance.
        /// </remarks>
        public static float SubStepsPerSecond
        {
            get => s_subStepsPerSecond;
            set => Mathf.Clamp(s_subStepsPerSecond, 1f, 1000f);
        }
        
        /// <summary>
        /// The maximum number of simulation substeps that can be run in a single step.
        /// </summary>
        /// <remarks>
        /// Decreasing this value helps to prevent spending excessive amounts of time simulating
        /// steps with large delta times at the cost of accuracy.
        /// </remarks>
        public static int MaxSubStepsPerFrame
        {
            get => s_maxSubStepsPerFrame;
            set => Mathf.Clamp(s_maxSubStepsPerFrame, 1, 1000);
        }

        static XpbdManager()
        {
            Application.quitting += OnQuit;
        }

        struct XpbdUpdate
        {
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init()
        {
            // In case the domain reload on enter play mode is disabled, we must
            // reset all static fields.
            DisposeResources();
            DisposeRegistrar();

            s_updateMode = XpbdUpdateMode.Automatic;
            s_subStepsPerSecond = 10 * 60;
            s_maxSubStepsPerFrame = 100;

            // inject the update method into the player loop
            PlayerLoopExtensions.RegisterUpdate<PostLateUpdate, XpbdUpdate>(Update);
            
            // enable the simulator by default
            Enable();
        }

        /// <summary>
        /// Checks if the current platform has support for the features required for the
        /// particle simulation implementation.
        /// </summary>
        /// <returns>True if the current platform is supported.</returns>
        public bool IsSupported()
        {
            return IsSupported(out _);
        }

        static void OnQuit()
        {
            DisposeResources();
            DisposeRegistrar();
        }

        /// <summary>
        /// Enable the particle simulator.
        /// </summary>
        public static void Enable()
        {
            if (!IsSupported(out var reasons))
            {
                Debug.LogWarning($"XPBD particles are not supported by the current platform: {reasons}");
                return;
            }
            if (!CreateResources())
            {
                return;
            }
            
            s_enabled = true;
            s_dirtyFlags = DirtyFlags.All;
            s_deltaTimeRemainder = 0f;
        }

        /// <summary>
        /// Disable the particle simulator.
        /// </summary>
        /// <remarks>
        /// Registered instances will not be cleared. 
        /// </remarks>
        /// <param name="disposeResources">Deallocate the resources used by the simulator.</param>
        public static void Disable(bool disposeResources)
        {
            s_enabled = false;

            if (disposeResources)
            {
                DisposeResources();
            }
        }

        /// <summary>
        /// Registers a cloth instance to the manager so it will be simulated.
        /// </summary>
        /// <param name="cloth">The cloth to register.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="cloth"/> is null.</exception>
        public static void RegisterCloth(ICloth cloth)
        {
            if (cloth == null)
            {
                throw new ArgumentNullException(nameof(cloth));
            }
            
            // prevent duplicate registration
            for (var i = 0; i < s_cloths.Count; i++)
            {
                if (s_cloths[i] == cloth)
                {
                    return;
                }
            }
            
            s_cloths.Add(cloth);
            s_clothStates.Add(new ClothState());
            s_dirtyFlags = DirtyFlags.All;
        }

        /// <summary>
        /// Deregisters a cloth instance from the manager so it is no longer simulated.
        /// </summary>
        /// <param name="cloth">The cloth to deregister.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="cloth"/> is null.</exception>
        public static void DeregisterCloth(ICloth cloth)
        {
            if (cloth == null)
            {
                throw new ArgumentNullException(nameof(cloth));
            }

            for (var i = 0; i < s_cloths.Count; i++)
            {
                if (s_cloths[i] == cloth)
                {
                    s_cloths.RemoveAt(i);
                    s_clothStates.RemoveAt(i);
                    s_dirtyFlags = DirtyFlags.All;
                    return;
                }
            }
        }
        
        /// <summary>
        /// Progresses the particle simulation.
        /// </summary>
        /// <remarks>
        /// This should only be called when <see cref="UpdateMode"/> is set to <see cref="XpbdUpdateMode.Manual"/>.
        /// </remarks>
        /// <param name="deltaTime">The number of seconds to advance the simulation.</param>
        public static void Simulate(float deltaTime)
        {
            if (!s_enabled)
            {
                return;
            }
            if (s_updateMode != XpbdUpdateMode.Manual)
            {
                Debug.LogError($"{nameof(Simulate)} can only be called when {nameof(s_updateMode)} is set to {nameof(XpbdUpdateMode.Manual)}.");
                return;
            }

            SimulateInternal(deltaTime);
        }

        static void Update()
        {
            if (!s_enabled)
            {
                return;
            }
            
            if (s_updateMode == XpbdUpdateMode.Automatic)
            {
                SimulateInternal(Time.deltaTime);
            }
        }
        
        static void SimulateInternal(float deltaTime)
        {
            using var _ = new ProfilerScope($"{nameof(XpbdManager)}.{nameof(SimulateInternal)}()");
            
            // advance the simulation time
            s_deltaTimeRemainder += Math.Max(deltaTime, 0f);
            var subStepCount = Mathf.FloorToInt(s_deltaTimeRemainder * s_subStepsPerSecond);
            
            float subStepDeltaTime;
            if (subStepCount > s_maxSubStepsPerFrame)
            {
                subStepCount = s_maxSubStepsPerFrame;
                subStepDeltaTime = s_deltaTimeRemainder / subStepCount;
            }
            else
            {
                subStepDeltaTime = 1f / s_subStepsPerSecond;
            }

            var stepDeltaTime = subStepCount * subStepDeltaTime;
            s_deltaTimeRemainder -= stepDeltaTime;
            
            // if there are no substeps, no need to update anything
            if (subStepCount <= 0)
            {
                return;
            }
            
            // check which buffers need to be updated, if any
            for (var i = 0; i < s_cloths.Count; i++)
            {
                var cloth = s_cloths[i];
                var dirtyFlags = cloth.DirtyFlags;

                if (dirtyFlags == ClothDirtyFlags.None)
                {
                    continue;
                }
                
                // get the modified state
                var state = s_clothStates[i];
                state.dirtyFlags = (DirtyFlags)dirtyFlags;
                s_clothStates[i] = state;
                
                s_dirtyFlags |= state.dirtyFlags;
                cloth.ClearDirtyFlags();
            }
            
            var cmdBuffer = CommandBufferPool.Get($"{nameof(XpbdManager)}_Simulate");
            
            // update any buffers whose contents is invalidated
            if (s_dirtyFlags.Intersects(DirtyFlags.UpdateBuffers))
            {
                UpdateBuffers();
            }
            else if (s_dirtyFlags.Intersects(DirtyFlags.ResetParticles))
            {
                ResetParticles(cmdBuffer);
            }
            
            if (s_dirtyFlags.Intersects(DirtyFlags.Gravity))
            {
                var gravity = new NativeArray<float4>(
                    s_cloths.Count,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory
                );
            
                for (var i = 0; i < s_cloths.Count; i++)
                {
                    gravity[i] = new float4(s_cloths[i].Gravity, 0f);
                }

                cmdBuffer.SetBufferData(s_gravityBuffer, gravity);
            }
            
            // execute the simulation kernels
            s_simulationProperties[0] = new SimulationPropertyBuffer
            {
                _DeltaTime = stepDeltaTime,
                _SubStepCount = (uint)subStepCount,
                _SubStepDeltaTime = subStepDeltaTime,
                _ParticleCount = (uint)s_particleCount,
                _DistanceConstraintCount = (uint)s_distanceConstraintCount,
            };
            
            cmdBuffer.SetBufferData(s_simulationConstantBuffer, s_simulationProperties);

            Graphics.ExecuteCommandBuffer(cmdBuffer);
            CommandBufferPool.Release(cmdBuffer);
            
            s_subStepCmdBuffer.Clear();
            
            for (var i = 0; i < subStepCount; i++)
            {
                // TODO don't recreate substep command buffer every frame 
                
                // integrate
                s_subStepCmdBuffer.SetComputeConstantBufferParam(
                    s_integrateShader,
                    Properties.Integrate._ConstantBuffer,
                    s_simulationConstantBuffer,
                    0,
                    SimulationPropertyBuffer.k_size
                );
                
                s_subStepCmdBuffer.SetComputeBufferParam(
                    s_integrateShader,
                    s_integrateKernel.kernelID,
                    Properties.Integrate._ParticleGroupIndices,
                    s_particleGroupIndicesBuffer
                );
                s_subStepCmdBuffer.SetComputeBufferParam(
                    s_integrateShader,
                    s_integrateKernel.kernelID,
                    Properties.Integrate._InverseMasses,
                    s_inverseMassesBuffer
                );
                s_subStepCmdBuffer.SetComputeBufferParam(
                    s_integrateShader,
                    s_integrateKernel.kernelID,
                    Properties.Integrate._Gravity,
                    s_gravityBuffer
                );
                s_subStepCmdBuffer.SetComputeBufferParam(
                    s_integrateShader,
                    s_integrateKernel.kernelID,
                    Properties.Integrate._Positions,
                    s_positionsBuffer
                );
                s_subStepCmdBuffer.SetComputeBufferParam(
                    s_integrateShader,
                    s_integrateKernel.kernelID,
                    Properties.Integrate._Velocities,
                    s_velocitiesBuffer
                );
            
                s_subStepCmdBuffer.DispatchCompute(
                    s_integrateShader,
                    s_integrateKernel.kernelID, 
                    s_integrateKernel.GetThreadGroupCount(s_particleCount), 1, 1
                );
                
                // solve constraints
                s_subStepCmdBuffer.SetComputeConstantBufferParam(
                    s_solveDistanceConstraintsShader,
                    Properties.SolveDistanceConstraints._ConstantBuffer,
                    s_simulationConstantBuffer,
                    0,
                    SimulationPropertyBuffer.k_size
                );
                
                s_subStepCmdBuffer.SetComputeBufferParam(
                    s_solveDistanceConstraintsShader,
                    s_solveDistanceConstraintsKernel.kernelID,
                    Properties.SolveDistanceConstraints._InverseMasses,
                    s_inverseMassesBuffer
                );
                s_subStepCmdBuffer.SetComputeBufferParam(
                    s_solveDistanceConstraintsShader,
                    s_solveDistanceConstraintsKernel.kernelID,
                    Properties.SolveDistanceConstraints._Constraints,
                    s_distanceConstraintsBuffer
                );
                s_subStepCmdBuffer.SetComputeBufferParam(
                    s_solveDistanceConstraintsShader,
                    s_solveDistanceConstraintsKernel.kernelID,
                    Properties.SolveDistanceConstraints._Positions,
                    s_positionsBuffer
                );
            
                s_subStepCmdBuffer.DispatchCompute(
                    s_solveDistanceConstraintsShader,
                    s_solveDistanceConstraintsKernel.kernelID, 
                    s_solveDistanceConstraintsKernel.GetThreadGroupCount(s_particleCount), 1, 1
                );
                
                // update velocities
                s_subStepCmdBuffer.SetComputeConstantBufferParam(
                    s_updateVelocitiesShader,
                    Properties.UpdateVelocities._ConstantBuffer,
                    s_simulationConstantBuffer,
                    0,
                    SimulationPropertyBuffer.k_size
                );
                
                s_subStepCmdBuffer.SetComputeBufferParam(
                    s_updateVelocitiesShader,
                    s_updateVelocitiesKernel.kernelID,
                    Properties.UpdateVelocities._Positions,
                    s_positionsBuffer
                );
                s_subStepCmdBuffer.SetComputeBufferParam(
                    s_updateVelocitiesShader,
                    s_updateVelocitiesKernel.kernelID,
                    Properties.UpdateVelocities._PrevPositions,
                    s_prevPositionsBuffer
                );
                s_subStepCmdBuffer.SetComputeBufferParam(
                    s_updateVelocitiesShader,
                    s_updateVelocitiesKernel.kernelID,
                    Properties.UpdateVelocities._Velocities,
                    s_velocitiesBuffer
                );
            
                s_subStepCmdBuffer.DispatchCompute(
                    s_updateVelocitiesShader,
                    s_updateVelocitiesKernel.kernelID, 
                    s_updateVelocitiesKernel.GetThreadGroupCount(s_particleCount), 1, 1
                );
            
                Graphics.ExecuteCommandBuffer(s_subStepCmdBuffer);
            }
            
            s_dirtyFlags = DirtyFlags.None;
        }

        static void UpdateBuffers()
        {
            using var _ = new ProfilerScope($"{nameof(XpbdManager)}.{nameof(UpdateBuffers)}()");

            // find the required buffer sizes
            s_particleCount = 0;
            s_distanceConstraintCount = 0;

            for (var i = 0; i < s_cloths.Count; i++)
            {
                var cloth = s_cloths[i];
                s_particleCount += cloth.ParticleCount;
                s_distanceConstraintCount += cloth.ConstraintCount;
            }
            
            // get the buffer data
            var particleGroupIndices = new NativeArray<uint>(
                s_particleCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory
            );
            var inverseMasses = new NativeArray<float>(
                s_particleCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory
            );
            var restPositions = new NativeArray<float4>(
                s_particleCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory
            );
            var distanceConstraints = new NativeArray<DistanceConstraint>(
                s_distanceConstraintCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory
            );

            var particleIndex = 0;
            var constraintIndex = 0;

            for (var i = 0; i < s_cloths.Count; i++)
            {
                var cloth = s_cloths[i];
                var clothParticles = cloth.GetParticles();
                var clothConstraints = cloth.GetConstraints();
                
                var baseIndex = particleIndex;

                for (var j = 0; j < clothParticles.Length; j++)
                {
                    var particle = clothParticles[j];

                    particleGroupIndices[particleIndex] = (uint)i;
                    inverseMasses[particleIndex] = particle.inverseMass;
                    restPositions[particleIndex] = new float4(particle.restPosition, 0f);
                    
                    particleIndex++;
                }
                for (var j = 0; j < clothConstraints.Length; j++)
                {
                    var constraint = clothConstraints[j];
                    
                    distanceConstraints[constraintIndex] = new DistanceConstraint
                    {
                        index0 = baseIndex + constraint.index0,
                        index1 = baseIndex + constraint.index1,
                        restLength = constraint.restLength,
                        compliance = constraint.compliance,
                    };

                    constraintIndex++;
                }
            }
            
            // create compute buffers
            if (s_gravityBuffer == null || s_gravityBuffer.count < s_cloths.Count)
            {
                DisposeUtils.DisposeSafe(ref s_gravityBuffer);
                
                var count = Mathf.NextPowerOfTwo(s_cloths.Count);
            
                s_gravityBuffer = new ComputeBuffer(
                    count,
                    4 * sizeof(float),
                    ComputeBufferType.Structured,
                    ComputeBufferMode.Dynamic
                )
                {
                    name = $"{nameof(XpbdManager)}_Gravity",
                };
            }

            if (s_particleGroupIndicesBuffer == null || s_particleGroupIndicesBuffer.count < s_particleCount)
            {
                DisposeUtils.DisposeSafe(ref s_particleGroupIndicesBuffer);
                DisposeUtils.DisposeSafe(ref s_inverseMassesBuffer);
                DisposeUtils.DisposeSafe(ref s_restPositionsBuffer);
                
                var count = Mathf.NextPowerOfTwo(s_particleCount);
            
                s_particleGroupIndicesBuffer = new ComputeBuffer(count, sizeof(uint))
                {
                    name = $"{nameof(XpbdManager)}_ParticleGroupIndices",
                };
                s_inverseMassesBuffer = new ComputeBuffer(count, sizeof(float))
                {
                    name = $"{nameof(XpbdManager)}_InverseMasses",
                };
                s_restPositionsBuffer = new ComputeBuffer(count, 4 * sizeof(float))
                {
                    name = $"{nameof(XpbdManager)}_RestPositions",
                };
            }
            
            if (s_distanceConstraintsBuffer == null || s_distanceConstraintsBuffer.count < s_distanceConstraintCount)
            {
                DisposeUtils.DisposeSafe(ref s_distanceConstraintsBuffer);

                var count = Mathf.NextPowerOfTwo(s_distanceConstraintCount);
            
                s_distanceConstraintsBuffer = new ComputeBuffer(count, DistanceConstraint.k_size)
                {
                    name = $"{nameof(XpbdManager)}_DistanceConstraints",
                };
            }
            
            // upload static data
            s_particleGroupIndicesBuffer.SetData(particleGroupIndices);
            s_inverseMassesBuffer.SetData(inverseMasses);
            s_restPositionsBuffer.SetData(restPositions);
            s_distanceConstraintsBuffer.SetData(distanceConstraints);

            particleGroupIndices.Dispose();
            inverseMasses.Dispose();
            restPositions.Dispose();
            distanceConstraints.Dispose();
            
            // copy GPU local data into new buffers
            var oldPositionsBuffer = s_positionsBuffer;
            var oldPrevPositionsBuffer = s_prevPositionsBuffer;
            var oldVelocitiesBuffer = s_velocitiesBuffer;
            
            var particleBuffersCount = Mathf.NextPowerOfTwo(s_particleCount);
            
            s_positionsBuffer = new ComputeBuffer(particleBuffersCount, 4 * sizeof(float))
            {
                name = $"{nameof(XpbdManager)}_Positions",
            };
            s_prevPositionsBuffer = new ComputeBuffer(particleBuffersCount, 4 * sizeof(float))
            {
                name = $"{nameof(XpbdManager)}_PrevPositions",
            };
            s_velocitiesBuffer = new ComputeBuffer(particleBuffersCount, 4 * sizeof(float))
            {
                name = $"{nameof(XpbdManager)}_Velocities",
            };

            var copies = new NativeList<(int baseIndex, int count)>(s_cloths.Count, Allocator.Temp);
            var resets = new NativeList<(int baseIndex, int count)>(s_cloths.Count, Allocator.Temp);

            {
                // Adjacent particle groups that are all being copied or reset can be handled in a single
                // kernel invocation. This increases efficiency, especially when the groups are smaller.
                var invocationIsCopy = false;
                var invocationBaseIndex = 0;
                var invocationSize = 0;
                var newBaseIndex = 0;

                for (var i = 0; i < s_cloths.Count; i++)
                {
                    var cloth = s_cloths[i];
                    var state = s_clothStates[i];

                    // reset existing particles if they were going to be reset anyway
                    var isCopy = state.isCreated && !state.dirtyFlags.Intersects(DirtyFlags.ResetParticles);

                    if (invocationSize == 0)
                    {
                        invocationIsCopy = isCopy;
                        invocationBaseIndex = state.particleBaseIndex;
                        invocationSize = state.particleCount;
                    }
                    else if (isCopy == invocationIsCopy)
                    {
                        invocationSize += state.particleCount;
                    }
                    else
                    {
                        if (invocationIsCopy)
                        {
                            copies.Add((invocationBaseIndex, invocationSize));
                        }
                        else
                        {
                            resets.Add((invocationBaseIndex, invocationSize));
                        }

                        invocationIsCopy = isCopy;
                        invocationBaseIndex = state.particleBaseIndex;
                        invocationSize = state.particleCount;
                    }

                    state.isCreated = true;
                    state.particleBaseIndex = newBaseIndex;
                    state.particleCount = cloth.ParticleCount;
                    s_clothStates[i] = state;

                    newBaseIndex += state.particleCount;
                }

                // complete the last invocation
                if (invocationSize > 0)
                {
                    if (invocationIsCopy)
                    {
                        copies.Add((invocationBaseIndex, invocationSize));
                    }
                    else
                    {
                        resets.Add((invocationBaseIndex, invocationSize));
                    }
                }
            }

            var copyDataCmdBuffer = CommandBufferPool.Get($"{nameof(XpbdManager)}_CopyData");
            
            copyDataCmdBuffer.SetComputeBufferParam(
                s_resetParticlesShader,
                s_copyParticlesKernel.kernelID,
                Properties.ResetParticles._OldPositions,
                oldPositionsBuffer
            );
            copyDataCmdBuffer.SetComputeBufferParam(
                s_resetParticlesShader,
                s_copyParticlesKernel.kernelID,
                Properties.ResetParticles._OldPrevPositions,
                oldPrevPositionsBuffer
            );
            copyDataCmdBuffer.SetComputeBufferParam(
                s_resetParticlesShader,
                s_copyParticlesKernel.kernelID,
                Properties.ResetParticles._OldVelocities,
                oldVelocitiesBuffer
            );
            copyDataCmdBuffer.SetComputeBufferParam(
                s_resetParticlesShader,
                s_copyParticlesKernel.kernelID,
                Properties.ResetParticles._Positions,
                s_positionsBuffer
            );
            copyDataCmdBuffer.SetComputeBufferParam(
                s_resetParticlesShader,
                s_copyParticlesKernel.kernelID,
                Properties.ResetParticles._PrevPositions,
                s_prevPositionsBuffer
            );
            copyDataCmdBuffer.SetComputeBufferParam(
                s_resetParticlesShader,
                s_copyParticlesKernel.kernelID,
                Properties.ResetParticles._Velocities,
                s_velocitiesBuffer
            );

            for (var i = 0; i < copies.Length; i++)
            {
                var copy = copies[i];
                
                copyDataCmdBuffer.SetComputeIntParam(
                    s_resetParticlesShader,
                    Properties.ResetParticles._BaseIndex,
                    copy.baseIndex
                );
                copyDataCmdBuffer.SetComputeIntParam(
                    s_resetParticlesShader,
                    Properties.ResetParticles._Count,
                    copy.count
                );

                copyDataCmdBuffer.DispatchCompute(
                    s_resetParticlesShader,
                    s_copyParticlesKernel.kernelID, 
                    s_copyParticlesKernel.GetThreadGroupCount(copy.count), 1, 1
                );
            }
            
            copyDataCmdBuffer.SetComputeBufferParam(
                s_resetParticlesShader,
                s_resetParticlesKernel.kernelID,
                Properties.ResetParticles._RestPositions,
                s_restPositionsBuffer
            );
            copyDataCmdBuffer.SetComputeBufferParam(
                s_resetParticlesShader,
                s_resetParticlesKernel.kernelID,
                Properties.ResetParticles._Positions,
                s_positionsBuffer
            );
            copyDataCmdBuffer.SetComputeBufferParam(
                s_resetParticlesShader,
                s_resetParticlesKernel.kernelID,
                Properties.ResetParticles._PrevPositions,
                s_prevPositionsBuffer
            );
            copyDataCmdBuffer.SetComputeBufferParam(
                s_resetParticlesShader,
                s_resetParticlesKernel.kernelID,
                Properties.ResetParticles._Velocities,
                s_velocitiesBuffer
            );
            
            for (var i = 0; i < resets.Length; i++)
            {
                var reset = resets[i];
                
                copyDataCmdBuffer.SetComputeIntParam(
                    s_resetParticlesShader,
                    Properties.ResetParticles._BaseIndex,
                    reset.baseIndex
                );
                copyDataCmdBuffer.SetComputeIntParam(
                    s_resetParticlesShader,
                    Properties.ResetParticles._Count,
                    reset.count
                );
                
                copyDataCmdBuffer.DispatchCompute(
                    s_resetParticlesShader,
                    s_resetParticlesKernel.kernelID, 
                    s_resetParticlesKernel.GetThreadGroupCount(reset.count), 1, 1
                );
            }

            Graphics.ExecuteCommandBuffer(copyDataCmdBuffer);
            CommandBufferPool.Release(copyDataCmdBuffer);
            
            // TODO: validate this is safe to do here. Also check if we can pass in the command buffer to be executed later
            // and still do this
            DisposeUtils.DisposeSafe(ref oldPositionsBuffer);
            DisposeUtils.DisposeSafe(ref oldPrevPositionsBuffer);
            DisposeUtils.DisposeSafe(ref oldVelocitiesBuffer);

            copies.Dispose();
            resets.Dispose();
        }

        static void ResetParticles(CommandBuffer cmdBuffer)
        {
            using var _ = new ProfilerScope($"{nameof(XpbdManager)}.{nameof(ResetParticles)}()");

            var resets = new NativeList<(int baseIndex, int count)>(s_cloths.Count, Allocator.Temp);

            // Adjacent particle groups that are all being copied or reset can be handled in a single
            // kernel invocation. This increases efficiency, especially when the groups are smaller.
            var invocationBaseIndex = 0;
            var invocationSize = 0;

            for (var i = 0; i < s_cloths.Count; i++)
            {
                var state = s_clothStates[i];

                if (state.isCreated && state.dirtyFlags.Intersects(DirtyFlags.ResetParticles))
                {
                    if (invocationSize == 0)
                    {
                        invocationBaseIndex = state.particleBaseIndex;
                        invocationSize = state.particleCount;
                    }
                    else
                    {
                        invocationSize += state.particleCount;
                    }
                }
                else if (invocationSize > 0)
                {
                    resets.Add((invocationBaseIndex, invocationSize));
                    invocationSize = 0;
                }
            }

            // complete the last invocation
            if (invocationSize > 0)
            {
                resets.Add((invocationBaseIndex, invocationSize));
            }

            cmdBuffer.SetComputeBufferParam(
                s_resetParticlesShader,
                s_resetParticlesKernel.kernelID,
                Properties.ResetParticles._RestPositions,
                s_restPositionsBuffer
            );
            cmdBuffer.SetComputeBufferParam(
                s_resetParticlesShader,
                s_resetParticlesKernel.kernelID,
                Properties.ResetParticles._Positions,
                s_positionsBuffer
            );
            cmdBuffer.SetComputeBufferParam(
                s_resetParticlesShader,
                s_resetParticlesKernel.kernelID,
                Properties.ResetParticles._PrevPositions,
                s_prevPositionsBuffer
            );
            cmdBuffer.SetComputeBufferParam(
                s_resetParticlesShader,
                s_resetParticlesKernel.kernelID,
                Properties.ResetParticles._Velocities,
                s_velocitiesBuffer
            );
            
            for (var i = 0; i < resets.Length; i++)
            {
                var reset = resets[i];
                
                cmdBuffer.SetComputeIntParam(
                    s_resetParticlesShader,
                    Properties.ResetParticles._BaseIndex,
                    reset.baseIndex
                );
                cmdBuffer.SetComputeIntParam(
                    s_resetParticlesShader,
                    Properties.ResetParticles._Count,
                    reset.count
                );
                
                cmdBuffer.DispatchCompute(
                    s_resetParticlesShader,
                    s_resetParticlesKernel.kernelID, 
                    s_resetParticlesKernel.GetThreadGroupCount(reset.count), 1, 1
                );
            }
        }
        
        static bool CreateResources()
        {
            if (s_resourcesInitialized)
            {
                return true;
            }

            // load the compute shaders
            s_resources = Resources.Load<XpbdResources>(nameof(XpbdResources));

            if (s_resources == null)
            {
                Debug.LogError("Failed to load XPBD resources!");
                DisposeResources();
                return false;
            }

            s_resetParticlesShader = s_resources.ResetParticles;
            s_integrateShader = s_resources.Integrate;
            s_solveDistanceConstraintsShader = s_resources.SolveDistanceConstraints;
            s_updateVelocitiesShader = s_resources.UpdateVelocities;

            if (s_resetParticlesShader == null ||
                s_integrateShader == null ||
                s_solveDistanceConstraintsShader == null ||
                s_updateVelocitiesShader == null
            )
            {
                Debug.LogError("Required compute shaders have not been assigned to the XPBD resources asset!");
                DisposeResources();
                return false;
            }

            if (!GraphicsUtils.TryGetKernel(s_resetParticlesShader, Kernels.ResetParticles.k_reset, out s_resetParticlesKernel) ||
                !GraphicsUtils.TryGetKernel(s_resetParticlesShader, Kernels.ResetParticles.k_copy, out s_copyParticlesKernel) ||
                !GraphicsUtils.TryGetKernel(s_integrateShader, Kernels.Integrate.k_main, out s_integrateKernel) ||
                !GraphicsUtils.TryGetKernel(s_solveDistanceConstraintsShader, Kernels.SolveDistanceConstraints.k_main, out s_solveDistanceConstraintsKernel) ||
                !GraphicsUtils.TryGetKernel(s_updateVelocitiesShader, Kernels.UpdateVelocities.k_main, out s_updateVelocitiesKernel)
            )
            {
                DisposeResources();
                return false;
            }

            // create command buffers
            s_subStepCmdBuffer = new CommandBuffer
            {
                name = $"{nameof(XpbdManager)}_SubStep",
            };
            
            // create constant buffers
            s_simulationProperties = new NativeArray<SimulationPropertyBuffer>(
                1,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory
            );
            
            s_simulationConstantBuffer = new ComputeBuffer(
                s_simulationProperties.Length,
                SimulationPropertyBuffer.k_size,
                ComputeBufferType.Constant
            )
            {
                name = $"{nameof(XpbdManager)}_{nameof(SimulationPropertyBuffer)}",
            };

            s_resourcesInitialized = true;
            return true;
        }

        static void DisposeResources()
        {
            s_enabled = false;
            s_dirtyFlags = DirtyFlags.All;
            
            s_resourcesInitialized = false;

            // clean up compute shaders
            if (s_resources != null)
            {
                Resources.UnloadAsset(s_resources);
                s_resources = null;
            }

            s_resetParticlesShader = null;
            s_integrateShader = null;
            s_solveDistanceConstraintsShader = null;
            s_updateVelocitiesShader = null;

            s_resetParticlesKernel = default;
            s_copyParticlesKernel = default;
            s_integrateKernel = default;
            s_solveDistanceConstraintsKernel = default;
            s_updateVelocitiesKernel = default;

            // dispose command buffers
            DisposeUtils.DisposeSafe(ref s_subStepCmdBuffer);

            // dispose constant buffers
            DisposeUtils.DisposeSafe(ref s_simulationProperties);
            DisposeUtils.DisposeSafe(ref s_simulationConstantBuffer);

            // dispose compute buffers
            DisposeUtils.DisposeSafe(ref s_particleGroupIndicesBuffer);
            DisposeUtils.DisposeSafe(ref s_inverseMassesBuffer);
            DisposeUtils.DisposeSafe(ref s_restPositionsBuffer);
            DisposeUtils.DisposeSafe(ref s_distanceConstraintsBuffer);

            DisposeUtils.DisposeSafe(ref s_gravityBuffer);
            
            DisposeUtils.DisposeSafe(ref s_positionsBuffer);
            DisposeUtils.DisposeSafe(ref s_prevPositionsBuffer);
            DisposeUtils.DisposeSafe(ref s_velocitiesBuffer);

            // clear managed state
            s_particleCount = 0;
            s_distanceConstraintCount = 0;
        }

        static void DisposeRegistrar()
        {
            s_cloths.Clear();
            s_clothStates.Clear();
        }
        
        static bool IsSupported(out string reasons)
        {
            reasons = null;

            if (!SystemInfo.supportsComputeShaders)
            {
                reasons = (reasons ?? string.Empty) + "Compute shaders are not supported!\n";
            }
            if (!SystemInfo.supportsSetConstantBuffer)
            {
                reasons = (reasons ?? string.Empty) + "Set constant buffer is not supported!\n";
            }
            
            return reasons == null;
        }
    }
}
