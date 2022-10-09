using System.Threading;

using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

using UnityEngine;

namespace Scsewell.PositionBasedDynamics.Core
{
    public abstract class Cloth : MonoBehaviour
    {
        const int k_maxBatchSize = 1024 * 16;

        struct BoundsData
        {
            public float3 min;
            public float3 size;
        }

        [SerializeField, Range(1, 64)]
        int m_subStepCount = 10;
        [SerializeField]
        Vector3 m_gravity = new Vector3(0, -9.81f, 0);
        [SerializeField]
        float m_thickness = 0.01f;
        [SerializeField]
        float m_spacing = 0.01f;
        [SerializeField]
        bool m_enableCollisions = true;

        bool m_resourcesCreated;
        bool m_resourcesDirty;
        
        NativeArray<long> m_restPositions;
        NativeArray<float> m_inverseMasses;
        NativeArray<long> m_positions;
        NativeArray<long> m_prevPositions;
        NativeArray<float3> m_velocities;
        NativeArray<float3> m_unpackedPositions;
        
        NativeArray<DistanceConstraint> m_constraints;
        
        NativeMultiHashMap<uint, int> m_cellToParticles;
        NativeMultiHashMap<int, int> m_collisionQueries;

        BuildCellToParticlesJob m_buildCellToParticlesJob;
        ComputeCollisionQueriesJob m_computeCollisionQueriesJob;
        IntegrateParticlesJob m_integrateParticlesJob;
        SolveConstraintsJob m_solveConstraintsJob;
        SolveCollisionsJob m_solveCollisionsJob;
        UpdateParticleVelocitiesJob m_updateParticleVelocitiesJob;
        UnpackPositionsJob m_unpackPositionsJob;

        public float Thickness
        {
            get => m_thickness;
            set => m_thickness = value;
        }
        
        public float Spacing
        {
            get => m_spacing;
            set => m_spacing = value;
        }
        
        public bool EnableCollisions
        {
            get => m_enableCollisions;
            set => m_enableCollisions = value;
        }

        protected virtual void OnValidate()
        {
            m_resourcesDirty = true;
        }

        protected virtual void OnEnable()
        {
            m_resourcesDirty = true;
        }

        protected virtual void OnDisable()
        {
            DisposeResources();
        }

        protected virtual void LateUpdate()
        {
            if (m_resourcesDirty)
            {
                CreateResources();
                m_resourcesDirty = false;
            }

            Simulate();
        }

        /// <summary>
        /// Resets the cloth back to the rest position.
        /// </summary>
        public void ResetSimulation()
        {
            //m_resetDirty
        }

        void CreateResources()
        {
            if (m_resourcesCreated)
            {
                DisposeResources();
            }

            GetCloth(out var bounds, out var particles, out var constraints);

            var boundsData = new BoundsData
            {
                min = bounds.min,
                size = bounds.size,
            };
            
            m_restPositions = new NativeArray<long>(
                particles.Length,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory
            );
            m_inverseMasses = new NativeArray<float>(
                particles.Length,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory
            );
            m_positions = new NativeArray<long>(
                particles.Length,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory
            );
            m_prevPositions = new NativeArray<long>(
                particles.Length,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory
            );
            m_velocities = new NativeArray<float3>(
                particles.Length,
                Allocator.Persistent
            );
            m_unpackedPositions = new NativeArray<float3>(
                particles.Length,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory
            );

            for (var i = 0; i < particles.Length; i++)
            {
                var particle = particles[i];
                var restPosition = PackPosition(particle.restPosition, boundsData);
                
                m_restPositions[i] = restPosition;
                m_positions[i] = restPosition;
                m_prevPositions[i] = restPosition;
                m_inverseMasses[i] = particle.inverseMass;
            }

            m_constraints = new NativeArray<DistanceConstraint>(
                constraints.Length,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory
            );

            for (var i = 0; i < constraints.Length; i++)
            {
                m_constraints[i] = constraints[i];
            }

            m_cellToParticles = new NativeMultiHashMap<uint, int>(2 * particles.Length, Allocator.Persistent);
            m_collisionQueries = new NativeMultiHashMap<int, int>(50 * particles.Length, Allocator.Persistent);
            
            m_buildCellToParticlesJob = new BuildCellToParticlesJob
            {
                bounds = boundsData,
                positions = m_positions,
                cellToParticles = m_cellToParticles.AsParallelWriter(),
            };
            m_computeCollisionQueriesJob = new ComputeCollisionQueriesJob
            {
                bounds = boundsData,
                positions = m_positions,
                cellToParticles = m_cellToParticles,
                collisionQueries = m_collisionQueries.AsParallelWriter(),
            };
            m_integrateParticlesJob = new IntegrateParticlesJob
            {
                bounds = boundsData,
                positions = m_positions,
                inverseMasses = m_inverseMasses,
                velocities = m_velocities,
                prevPositions = m_prevPositions,
            };
            m_solveConstraintsJob = new SolveConstraintsJob
            {
                bounds = boundsData,
                inverseMasses = m_inverseMasses,
                positions = m_positions,
                constraints = m_constraints,
            };
            m_solveCollisionsJob = new SolveCollisionsJob
            {
                bounds = boundsData,
                inverseMasses = m_inverseMasses,
                restPositions = m_restPositions,
                positions = m_positions,
                prevPositions = m_prevPositions,
                collisionQueries = m_collisionQueries,
            };
            m_updateParticleVelocitiesJob = new UpdateParticleVelocitiesJob
            {
                bounds = boundsData,
                positions = m_positions,
                prevPositions = m_prevPositions,
                velocities = m_velocities,
            };
            m_unpackPositionsJob = new UnpackPositionsJob
            {
                bounds = boundsData,
                packedPositions = m_positions,
                unpackedPositions = m_unpackedPositions,
            };

            m_resourcesCreated = true;
        }

        void DisposeResources()
        {
            if (!m_resourcesCreated)
            {
                return;
            }
            
            m_restPositions.Dispose();
            m_inverseMasses.Dispose();
            m_positions.Dispose();
            m_prevPositions.Dispose();
            m_velocities.Dispose();
            m_unpackedPositions.Dispose();
            
            m_constraints.Dispose();

            m_cellToParticles.Dispose();
            m_collisionQueries.Dispose();

            m_resourcesCreated = false;
        }

        void Simulate()
        {
            var deltaTime = Time.deltaTime;
            
            var subStepDeltaTime = deltaTime / m_subStepCount;
            var maxSpeed = 0.2f * m_thickness / subStepDeltaTime;
            var maxDistance = maxSpeed * deltaTime;
            
            m_buildCellToParticlesJob.spacing = m_spacing;
            
            m_computeCollisionQueriesJob.spacing = m_spacing;
            m_computeCollisionQueriesJob.maxDistance = maxDistance;
            
            m_solveCollisionsJob.thickness = m_thickness;

            m_integrateParticlesJob.deltaTime = subStepDeltaTime;
            m_integrateParticlesJob.gravity = m_gravity;
            m_integrateParticlesJob.maxSpeed = maxSpeed;

            m_solveConstraintsJob.deltaTime = subStepDeltaTime;

            m_updateParticleVelocitiesJob.deltaTime = subStepDeltaTime;

            var integrateParticlesDependencies = default(JobHandle);
            
            if (m_enableCollisions)
            {
                m_cellToParticles.Clear();
                m_collisionQueries.Clear();

                var buildCellsToParticlesJob = m_buildCellToParticlesJob.ScheduleByRef(
                    m_positions.Length,
                    128
                );
                var computeCollisionQueriesJob = m_computeCollisionQueriesJob.ScheduleByRef(
                    m_positions.Length,
                    8,
                    buildCellsToParticlesJob
                );

                integrateParticlesDependencies = computeCollisionQueriesJob;
            }
            
            for (var subStep = 0; subStep < m_subStepCount; subStep++)
            {
                var integrateParticlesJob = m_integrateParticlesJob.ScheduleByRef(
                    m_positions.Length,
                    128,
                    integrateParticlesDependencies
                );
                var solveConstraintsJob = m_solveConstraintsJob.ScheduleByRef(
                    m_constraints.Length,
                    512,
                    integrateParticlesJob
                );

                var updateParticleVelocitiesDependencies = solveConstraintsJob;
                
                if (m_enableCollisions)
                {
                    var solveCollisionsJob = m_solveCollisionsJob.ScheduleByRef(
                        m_positions.Length,
                        128,
                        solveConstraintsJob
                    );

                    updateParticleVelocitiesDependencies = solveCollisionsJob;
                }

                var updateParticleVelocitiesJob = m_updateParticleVelocitiesJob.ScheduleByRef(
                    m_positions.Length,
                    128,
                    updateParticleVelocitiesDependencies
                );

                integrateParticlesDependencies = updateParticleVelocitiesJob;
            }
            
            var unpackPositionsJob = m_unpackPositionsJob.ScheduleByRef(
                m_positions.Length,
                128,
                integrateParticlesDependencies
            );
            
            unpackPositionsJob.Complete();
            
            ClothUpdated(m_unpackedPositions);
        }

        /// <summary>
        /// Gets the parameters of the cloth to be simulated.
        /// </summary>
        /// <param name="bounds">The bounds of the cloth simulation.</param>
        /// <param name="particles">The cloth particles.</param>
        /// <param name="constraints">The constraints used when simulating the cloth.</param>
        protected abstract void GetCloth(
            out Bounds bounds,
            out NativeSlice<ClothParticle> particles,
            out NativeSlice<DistanceConstraint> constraints
        );

        /// <summary>
        /// Called after the cloth simulation has progressed.
        /// </summary>
        /// <param name="positions">The updated positions of the cloth particles.</param>
        protected abstract void ClothUpdated(NativeSlice<float3> positions);

        [BurstCompile(DisableSafetyChecks = true, FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low)]
        struct BuildCellToParticlesJob : IJobParallelFor
        {
            public BoundsData bounds;
            public float spacing;
            
            [ReadOnly, NoAlias]
            public NativeArray<long> positions;
            
            [WriteOnly, NoAlias]
            public NativeMultiHashMap<uint, int>.ParallelWriter cellToParticles;
            
            public void Execute(int i)
            {
                var position = UnpackPosition(positions[i], bounds);
                var cell = GetCell(position, spacing);
                var hash = math.hash(cell);
                cellToParticles.Add(hash, i);
            }
        }
        
        [BurstCompile(DisableSafetyChecks = true, FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low)]
        struct ComputeCollisionQueriesJob : IJobParallelFor
        {
            public BoundsData bounds;
            public float spacing;
            public float maxDistance;
            
            [ReadOnly, NoAlias]
            public NativeArray<long> positions;
            [ReadOnly, NoAlias]
            public NativeMultiHashMap<uint, int> cellToParticles;
            
            [WriteOnly, NoAlias]
            public NativeMultiHashMap<int, int>.ParallelWriter collisionQueries;

            public void Execute(int id0)
            {
                var maxDistance2 = maxDistance * maxDistance;

                var p0 = UnpackPosition(positions[id0], bounds);
                var minCell = GetCell(p0 - maxDistance, spacing);
                var maxCell = GetCell(p0 + maxDistance, spacing);

                for (var xi = minCell.x; xi <= maxCell.x; xi++)
                {
                    for (var yi = minCell.y; yi <= maxCell.y; yi++)
                    {
                        for (var zi = minCell.z; zi <= maxCell.z; zi++)
                        {
                            var cell = new int3(xi, yi, zi);
                            var hash = math.hash(cell);

                            foreach (var id1 in cellToParticles.GetValuesForKey(hash))
                            {
                                var p1 = UnpackPosition(positions[id1], bounds);

                                if (math.distancesq(p0, p1) < maxDistance2)
                                {
                                    collisionQueries.Add(id0, id1);
                                }
                            }
                        }
                    }
                }
            }
        }

        [BurstCompile(DisableSafetyChecks = true, FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low)]
        struct IntegrateParticlesJob : IJobParallelFor
        {
            public BoundsData bounds;
            public float deltaTime;
            public float3 gravity;
            public float maxSpeed;
            
            [ReadOnly, NoAlias]
            public NativeArray<float> inverseMasses;
            [ReadOnly, NoAlias]
            public NativeArray<float3> velocities;

            [NoAlias]
            public NativeArray<long> positions;
            
            
            [WriteOnly, NoAlias]
            public NativeArray<long> prevPositions;
            
            public void Execute(int i)
            {
                if (inverseMasses[i] == 0f)
                {
                    return;
                }

                var packedPos = positions[i];
                
                
                // TODO: MOVE THIS TO END OF LOOP
                prevPositions[i] = packedPos;
                
                var position = UnpackPosition(packedPos, bounds);
                var velocity = velocities[i] + (gravity * deltaTime);
                var speed = math.length(velocity);
                
                if (speed > maxSpeed)
                {
                    velocity *= maxSpeed / speed;
                }

                var newPosition = position + (velocity * deltaTime);
                
                positions[i] = PackPosition(newPosition, bounds);
            }
        }

        [BurstCompile(DisableSafetyChecks = true, FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low)]
        struct SolveConstraintsJob : IJobParallelFor
        {
            public BoundsData bounds;
            public float deltaTime;
            
            [ReadOnly, NoAlias]
            public NativeArray<float> inverseMasses;
            [ReadOnly, NoAlias]
            public NativeArray<DistanceConstraint> constraints;
            
            [NativeDisableParallelForRestriction, NoAlias]
            public NativeArray<long> positions;

            public void Execute(int i)
            {
                var constraint = constraints[i];

                var w0 = inverseMasses[constraint.index0];
                var w1 = inverseMasses[constraint.index1];

                var w = w0 + w1;

                if (w == 0f)
                {
                    return;
                }

                while (true)
                {
                    var packedP0 = positions[constraint.index0];
                    var packedP1 = positions[constraint.index1];

                    var p0 = UnpackPosition(packedP0, bounds);
                    var p1 = UnpackPosition(packedP1, bounds);

                    var disp = p0 - p1;
                    var len = math.length(disp);

                    if (len == 0f)
                    {
                        return;
                    }
                 
                    var dir = disp / len;
                    var c = len - constraint.restLength;
                    var alpha = constraint.compliance / (deltaTime * deltaTime);
                    var s = -c / (w + alpha);
                    var newP0 = p0 + (dir * (s * w0));
                    var newP1 = p1 + (dir * (-s * w1));
                    
                    if (AtomicWrite(positions, constraint.index0, PackPosition(newP0, bounds), packedP0) && 
                        AtomicWrite(positions, constraint.index1, PackPosition(newP1, bounds), packedP1))
                    {
                        return;
                    }
                }
            }
        }
        
        [BurstCompile(DisableSafetyChecks = true, FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low)]
        struct SolveCollisionsJob : IJobParallelFor
        {
            public BoundsData bounds;
            public float thickness;

            [ReadOnly, NoAlias]
            public NativeArray<float> inverseMasses;
            [ReadOnly, NoAlias]
            public NativeArray<long> restPositions;
            [ReadOnly, NoAlias]
            public NativeArray<long> prevPositions;
            [ReadOnly, NoAlias]
            public NativeMultiHashMap<int, int> collisionQueries;

            [NativeDisableParallelForRestriction, NoAlias]
            public NativeArray<long> positions;

            public void Execute(int id0)
            {
                if (inverseMasses[id0] == 0f)
                {
                    return;
                }

                var thickness2 = thickness * thickness;

                foreach (var id1 in collisionQueries.GetValuesForKey(id0))
                {
                    if (inverseMasses[id1] == 0f)
                    {
                        continue;
                    }
                    
                    while (true)
                    {
                        var packedP0 = positions[id0];
                        var packedP1 = positions[id1];

                        var p0 = UnpackPosition(packedP0, bounds);
                        var p1 = UnpackPosition(packedP1, bounds);

                        var disp = p1 - p0;
                        var dist2 = math.lengthsq(disp);
                    
                        if (dist2 > thickness2 || dist2 == 0f)
                        {
                            continue;
                        }

                        var r0 = restPositions[id0];
                        var r1 = restPositions[id1];

                        var restDist2 = math.distancesq(r0, r1);
                    
                        if (restDist2 < dist2)
                        {
                            continue;
                        }

                        var minDist = thickness;
                    
                        if (restDist2 < thickness2)
                        {
                            minDist = math.sqrt(restDist2);
                        }

                        // position correction
                        var dist = math.sqrt(dist2);
                        var pDelta = disp * (0.5f * (minDist - dist) / dist);

                        p0 -= pDelta;
                        p1 += pDelta;

                        // velocity correction
                        var v0 = p0 - prevPositions[id0];
                        var v1 = p1 - prevPositions[id1];
                        var vAvg = 0.5f * (v0 + v1);

                        var friction = 0f;
                    
                        var newP0 = p0 + (friction * (vAvg - v0));
                        var newP1 = p1 + (friction * (vAvg - v1));
                        
                        if (AtomicWrite(positions, id0, PackPosition(newP0, bounds), packedP0) && 
                            AtomicWrite(positions, id1, PackPosition(newP1, bounds), packedP1))
                        {
                            return;
                        }
                    }
                }
            }
        }
        
        [BurstCompile(DisableSafetyChecks = true, FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low)]
        struct UpdateParticleVelocitiesJob : IJobParallelFor
        {
            public BoundsData bounds;
            public float deltaTime;
            
            [ReadOnly, NoAlias]
            public NativeArray<long> positions;
            [ReadOnly, NoAlias]
            public NativeArray<long> prevPositions;
            
            [WriteOnly, NoAlias]
            public NativeArray<float3> velocities;

            public void Execute(int i)
            {
                var pos = UnpackPosition(positions[i], bounds);
                var prevPos = UnpackPosition(prevPositions[i], bounds);
                
                velocities[i] = (pos - prevPos) / deltaTime;
            }
        }
        
        [BurstCompile(DisableSafetyChecks = true, FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low)]
        struct UnpackPositionsJob : IJobParallelFor
        {
            public BoundsData bounds;
            
            [ReadOnly, NoAlias]
            public NativeArray<long> packedPositions;
            
            [WriteOnly, NoAlias]
            public NativeArray<float3> unpackedPositions;

            public void Execute(int i)
            {
                unpackedPositions[i] = UnpackPosition(packedPositions[i], bounds);
            }
        }
        
        static long PackPosition(float3 position, BoundsData boundsData)
        {
            var relativePosition = (position - boundsData.min) / boundsData.size;
            var packed = math.int3(math.round(math.saturate(relativePosition) * 0x1FFFFF));

            return ((long)packed.z << 42) | ((long)packed.y << 21) | (long)packed.x;
        }

        static float3 UnpackPosition(long packedPosition, BoundsData boundsData)
        {
            var unpacked = new float3(
                ((packedPosition >> 0 ) & 0x1FFFFF),
                ((packedPosition >> 21) & 0x1FFFFF),
                ((packedPosition >> 42) & 0x1FFFFF)
            ) / 0x1FFFFF;

            return boundsData.min + unpacked * boundsData.size;
        }

        static unsafe bool AtomicWrite(NativeArray<long> array, int index, long value, long initialValue)
        {
            ref var ptr = ref UnsafeUtility.ArrayElementAsRef<long>(array.GetUnsafePtr(), index);
            return initialValue == Interlocked.CompareExchange(ref ptr, value, initialValue);
        }

        static int3 GetCell(float3 position, float spacing)
        {
            return math.int3(math.floor(position / spacing));
        }
    }
}
