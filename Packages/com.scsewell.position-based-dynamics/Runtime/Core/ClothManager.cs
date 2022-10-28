using System;
using System.Collections.Generic;

using Unity.Collections;

using UnityEngine;
using UnityEngine.PlayerLoop;

namespace Scsewell.PositionBasedDynamics
{
    /// <summary>
    /// An enum that defines the options for progressing the particle simulation.
    /// </summary>
    public enum ClothUpdateMode
    {
        /// <summary>
        /// The simulation is automatically progressed every frame using a fixed timestep.
        /// </summary>
        Automatic,
        
        /// <summary>
        /// The simulation is progressed by calling <see cref="ClothManager.Simulate"/> on demand.
        /// </summary>
        Manual,
    }
    
    /// <summary>
    /// A class that manages the GPU cloth simulation.
    /// </summary>
    public class ClothManager
    {
        // shader resources
        static bool s_resourcesInitialized;
        static ClothResources s_resources;
        internal static ComputeShader s_clothShader;
        internal static KernelInfo s_clothKernel;
        
        // constant buffers
        static NativeArray<SimulationPropertyBuffer> s_simulationProperties;
        internal static GraphicsBuffer s_simulationConstantBuffer;
        
        static bool s_enabled;
        static float s_deltaTimeRemainder;
        static ClothUpdateMode s_updateMode;
        static float s_subStepsPerSecond;
        static int s_maxSubStepsPerFrame;

        static readonly List<ClothState> s_cloths = new List<ClothState>();
        
        /// <summary>
        /// Is the simulator enabled.
        /// </summary>
        public static bool Enabled => s_enabled;

        /// <summary>
        /// Controls how the particle simulation is progressed.
        /// </summary>
        public static ClothUpdateMode UpdateMode
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

        static ClothManager()
        {
            Application.quitting += OnQuit;
        }

        struct ClothUpdate
        {
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init()
        {
            // In case the domain reload on enter play mode is disabled, we must
            // reset all static fields.
            DisposeResources();
            DisposeRegistrar();

            s_updateMode = ClothUpdateMode.Automatic;
            s_subStepsPerSecond = 10 * 60;
            s_maxSubStepsPerFrame = 100;

            // inject the update method into the player loop
            PlayerLoopExtensions.RegisterUpdate<PostLateUpdate, ClothUpdate>(Update);
            
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
        /// Enable the cloth simulator.
        /// </summary>
        public static void Enable()
        {
            if (!IsSupported(out var reasons))
            {
                Debug.LogWarning($"Cloth simulation is not supported by the current platform: {reasons}");
                return;
            }
            if (!CreateResources())
            {
                return;
            }
            
            s_enabled = true;
            s_deltaTimeRemainder = 0f;
        }

        /// <summary>
        /// Disable the cloth simulator.
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
                if (s_cloths[i].Cloth == cloth)
                {
                    return;
                }
            }
            
            s_cloths.Add(new ClothState(cloth));
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
                if (s_cloths[i].Cloth == cloth)
                {
                    s_cloths[i].Dispose();
                    s_cloths.RemoveAt(i);
                    return;
                }
            }
        }
        
        /// <summary>
        /// Progresses the cloth simulation.
        /// </summary>
        /// <remarks>
        /// This should only be called when <see cref="UpdateMode"/> is set to <see cref="ClothUpdateMode.Manual"/>.
        /// </remarks>
        /// <param name="deltaTime">The number of seconds to advance the simulation.</param>
        public static void Simulate(float deltaTime)
        {
            if (!s_enabled)
            {
                return;
            }
            if (s_updateMode != ClothUpdateMode.Manual)
            {
                Debug.LogError($"{nameof(Simulate)} can only be called when {nameof(s_updateMode)} is set to {nameof(ClothUpdateMode.Manual)}.");
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
            
            using var _ = new ProfilerScope($"{nameof(ClothManager)}.{nameof(Update)}()");

            if (s_updateMode == ClothUpdateMode.Automatic)
            {
                SimulateInternal(Time.deltaTime);
            }
            
            Render();
        }
        
        static void SimulateInternal(float deltaTime)
        {
            using var _ = new ProfilerScope($"{nameof(ClothManager)}.{nameof(SimulateInternal)}()");
            
            // advance the simulation time
            s_deltaTimeRemainder += Math.Max(deltaTime, 0f);
            
            var subStepCount = Mathf.FloorToInt(s_deltaTimeRemainder * s_subStepsPerSecond);
            
            if (subStepCount > s_maxSubStepsPerFrame)
            {
                subStepCount = s_maxSubStepsPerFrame;
            }
            
            var subStepDeltaTime = 1f / s_subStepsPerSecond;
            var stepDeltaTime = subStepCount * subStepDeltaTime;
            s_deltaTimeRemainder -= stepDeltaTime;
            
            // if there are no substeps, no need to update anything
            if (subStepCount <= 0)
            {
                return;
            }
            
            // update the simulation global parameters constant buffer
            var cmdBuffer = CommandBufferPool.Get($"{nameof(ClothManager)}_Update");
            
            s_simulationProperties[0] = new SimulationPropertyBuffer
            {
                _DeltaTime = stepDeltaTime,
                _SubStepCount = (uint)subStepCount,
                _SubStepDeltaTime = subStepDeltaTime,
            };
            
            cmdBuffer.SetBufferData(s_simulationConstantBuffer, s_simulationProperties);

            // update the cloth instances
            for (var i = 0; i < s_cloths.Count; i++)
            {
                s_cloths[i].Update(cmdBuffer);
            }
            
            Graphics.ExecuteCommandBuffer(cmdBuffer);
            CommandBufferPool.Release(cmdBuffer);
            
            for (var i = 0; i < s_cloths.Count; i++)
            {
                s_cloths[i].Simulate();
            }
        }

        static void Render()
        {
            using var _ = new ProfilerScope($"{nameof(ClothManager)}.{nameof(Render)}()");

            for (var i = 0; i < s_cloths.Count; i++)
            {
                s_cloths[i].Render();
            }
        }

        static bool CreateResources()
        {
            if (s_resourcesInitialized)
            {
                return true;
            }

            // load the compute shaders
            s_resources = Resources.Load<ClothResources>(nameof(ClothResources));

            if (s_resources == null)
            {
                Debug.LogError("Failed to load cloth resources!");
                DisposeResources();
                return false;
            }

            s_clothShader = s_resources.Cloth;

            if (s_clothShader == null)
            {
                Debug.LogError("Cloth compute shader resources are missing!");
                DisposeResources();
                return false;
            }

            if (!s_clothShader.TryGetKernel(Kernels.Cloth.main, out s_clothKernel))
            {
                DisposeResources();
                return false;
            }

            // create constant buffers
            s_simulationProperties = new NativeArray<SimulationPropertyBuffer>(
                1,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory
            );
            
            s_simulationConstantBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Constant,
                GraphicsBuffer.UsageFlags.LockBufferForWrite,
                s_simulationProperties.Length,
                SimulationPropertyBuffer.k_size
            )
            {
                name = $"{nameof(ClothManager)}_{nameof(SimulationPropertyBuffer)}",
            };
            
            s_resourcesInitialized = true;
            return true;
        }

        static void DisposeResources()
        {
            s_enabled = false;
            s_resourcesInitialized = false;

            // clean up compute shaders
            if (s_resources != null)
            {
                Resources.UnloadAsset(s_resources);
                s_resources = null;
            }

            s_clothShader = null;
            s_clothKernel = default;

            // dispose constant buffers
            DisposeUtils.DisposeSafe(ref s_simulationProperties);
            DisposeUtils.DisposeSafe(ref s_simulationConstantBuffer);
        }

        static void DisposeRegistrar()
        {
            for (var i = 0; i < s_cloths.Count; i++)
            {
                s_cloths[i].Dispose();
            }
            
            s_cloths.Clear();
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
