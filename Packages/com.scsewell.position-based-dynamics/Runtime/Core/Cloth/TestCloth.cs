using System;
using System.Collections.Generic;

using Unity.Collections;
using Unity.Mathematics;

using UnityEngine;

namespace Scsewell.PositionBasedDynamics
{
    class TestCloth : MonoBehaviour, ICloth
    {
        [Flags]
        public enum ConstraintTypes
        {
            None = 0,
            Stretch = 1 << 0,
            Shear = 1 << 1,
            Bending = 1 << 2,
            All = ~0,
        }

        struct ConstraintBatch
        {
            public readonly ConstraintTypes type;
            public readonly int4 offsets;
            public readonly Func<int, int, bool> addToBatch;
            public int size;

            public ConstraintBatch(ConstraintTypes type, int4 offsets, Func<int, int, bool> addToBatch)
            {
                this.type = type;
                this.offsets = offsets;
                this.addToBatch = addToBatch;
                size = 0;
            }
        }
        
        [SerializeField]
        Material m_material;
        [SerializeField, Range(5, 200)]
        int m_resX = 30;
        [SerializeField, Range(5, 200)]
        int m_resY = 30;
        [SerializeField]
        float m_spacing = 0.01f;
        [SerializeField]
        Bounds m_bounds = new Bounds(Vector3.zero, Vector3.one);
        [SerializeField]
        ConstraintTypes m_EnabledConstraints = ConstraintTypes.All;
        [SerializeField, Range(0, 1f)]
        float m_stretchCompliance = 0f;
        [SerializeField, Range(0, 1f)]
        float m_shearCompliance = 0.25f;
        [SerializeField, Range(0, 1f)]
        float m_bendingCompliance = 0.25f;
        [SerializeField]
        bool m_hang = true;
        [SerializeField]
        bool m_jitter = false;
        [SerializeField]
        Vector3 m_gravity = new Vector3(0, -9.81f, 0);
        [SerializeField]
        bool m_debug = false;

        bool m_resourcesCreated;
        bool m_resourcesDirty;
        ClothDirtyFlags m_dirtyFlags;
        NativeArray<ClothParticle> m_particles;
        NativeArray<ClothConstraint> m_constraints;
        NativeArray<uint> m_indices;
        readonly List<ConstraintBatch> m_constraintBatches = new List<ConstraintBatch>();

        /// <inheritdoc />
        public string Name => name;

        /// <inheritdoc />
        public int ParticleCount => m_particles.Length;

        /// <inheritdoc />
        public int IndexCount => m_indices.Length;

        /// <inheritdoc />
        public int ConstraintBatchCount => m_constraintBatches.Count;

        /// <inheritdoc />
        public Bounds Bounds => m_bounds;

        /// <inheritdoc />
        public float3 Gravity => m_gravity;
        
        /// <inheritdoc />
        public Matrix4x4 Transform => transform.localToWorldMatrix;

        /// <inheritdoc />
        public Material Material => m_material;

        /// <inheritdoc />
        ClothDirtyFlags ICloth.DirtyFlags
        {
            get => m_dirtyFlags;
            set => m_dirtyFlags = value;
        }

        void OnValidate()
        {
            m_resourcesDirty = true;
        }
        
        void OnEnable()
        {
            m_resourcesDirty = true;
        }

        void OnDisable()
        {
            DisposeResources();
        }

        void LateUpdate()
        {
            if (m_resourcesDirty)
            {
                CreateResources();
                m_resourcesDirty = false;
            }
        }

        /// <inheritdoc />
        public NativeSlice<ClothParticle> GetParticles()
        {
            return m_particles.Slice();
        }

        /// <inheritdoc />
        public NativeSlice<uint> GetIndices()
        {
            return m_indices.Slice();
        }

        /// <inheritdoc />
        public int GetConstraintBatchSize(int batchIndex)
        {
            return m_constraintBatches[batchIndex].size;
        }

        /// <inheritdoc />
        public void GetConstraintBatch(int batchIndex, out NativeSlice<ClothConstraint> constraints, out float compliance)
        {
            var batch = m_constraintBatches[batchIndex];
            
            constraints = m_constraints.Slice(batchIndex * (m_particles.Length / 2), batch.size);
            
            // use an exponent to remap the compliance into a more intuitive range
            compliance = Mathf.Pow(batch.type switch
            {
                ConstraintTypes.Stretch => m_stretchCompliance,
                ConstraintTypes.Shear => m_shearCompliance,
                ConstraintTypes.Bending => m_bendingCompliance,
                _ => 0f,
            }, 4);
        }

        void CreateResources()
        {
            DisposeResources();

            // create particles
            m_particles = new NativeArray<ClothParticle>(
                m_resX * m_resY,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory
            );
            
            for (var i = 0; i < m_resX; i++)
            {
                for (var j = 0; j < m_resY; j++)
                {
                    var id = i * m_resY + j;

                    var position = new Vector3(
                        m_spacing * (-0.5f * (m_resX - 1) + i),
                        m_spacing * (-0.5f * (m_resY - 1) + j),
                        0f
                    );
                    if (m_jitter)
                    {
                        position += 0.001f * UnityEngine.Random.insideUnitSphere;
                    }
                    
                    m_particles[id] = new ClothParticle
                    {
                        restPosition = position,
                        inverseMass = (m_hang && j == (m_resY - 1) && (i == 0 || i == m_resX - 1)) ? 0f : 1f,
                    };
                }
            }

            // create constraints
            m_constraintBatches.Clear();
            
            if (m_EnabledConstraints.Intersects(ConstraintTypes.Stretch))
            {
                m_constraintBatches.Add(new(ConstraintTypes.Stretch, new int4(0, 0, 0, 1), (_, j) => j % 2 == 0));
                m_constraintBatches.Add(new(ConstraintTypes.Stretch, new int4(0, 0, 0, 1), (_, j) => j % 2 == 1));
                m_constraintBatches.Add(new(ConstraintTypes.Stretch, new int4(0, 0, 1, 0), (i, _) => i % 2 == 0));
                m_constraintBatches.Add(new(ConstraintTypes.Stretch, new int4(0, 0, 1, 0), (i, _) => i % 2 == 1));
            }
            if (m_EnabledConstraints.Intersects(ConstraintTypes.Shear))
            {
                m_constraintBatches.Add(new(ConstraintTypes.Shear, new int4(0, 0, 1, 1), (i, _) => i % 2 == 0));
                m_constraintBatches.Add(new(ConstraintTypes.Shear, new int4(0, 0, 1, 1), (i, _) => i % 2 == 1));
                m_constraintBatches.Add(new(ConstraintTypes.Shear, new int4(0, 1, 1, 0), (i, _) => i % 2 == 0));
                m_constraintBatches.Add(new(ConstraintTypes.Shear, new int4(0, 1, 1, 0), (i, _) => i % 2 == 1));
            }
            if (m_EnabledConstraints.Intersects(ConstraintTypes.Bending))
            {
                m_constraintBatches.Add(new(ConstraintTypes.Bending, new int4(0, 0, 0, 2), (_, j) => j % 4 < 2));
                m_constraintBatches.Add(new(ConstraintTypes.Bending, new int4(0, 0, 0, 2), (_, j) => j % 4 >= 2));
                m_constraintBatches.Add(new(ConstraintTypes.Bending, new int4(0, 0, 2, 0), (i, _) => i % 4 < 2));
                m_constraintBatches.Add(new(ConstraintTypes.Bending, new int4(0, 0, 2, 0), (i, _) => i % 4 >= 2));
            }

            var maxConstraintsPerBatch = m_particles.Length / 2;
            
            m_constraints = new NativeArray<ClothConstraint>(
                maxConstraintsPerBatch * m_constraintBatches.Count,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory
            );

            for (var b = 0; b < m_constraintBatches.Count; b++)
            {
                var batch = m_constraintBatches[b];
                
                for (var i = 0; i < m_resX; i++)
                {
                    for (var j = 0; j < m_resY; j++)
                    {
                        if (!batch.addToBatch(i, j))
                        {
                            continue;
                        }
                        
                        var offset = new int4(i, j, i, j) + batch.offsets;

                        if (offset.x >= m_resX ||
                            offset.y >= m_resY ||
                            offset.z >= m_resX ||
                            offset.w >= m_resY)
                        {
                            continue;
                        }

                        var id0 = offset.x * m_resY + offset.y;
                        var id1 = offset.z * m_resY + offset.w;
                        
                        var constraintIndex = (maxConstraintsPerBatch * b) + batch.size;

                        m_constraints[constraintIndex] = new ClothConstraint
                        {
                            index0 = id0,
                            index1 = id1,
                            restLength = math.distance(
                                m_particles[id0].restPosition,
                                m_particles[id1].restPosition
                            ),
                        };

                        batch.size++;
                    }
                }

                m_constraintBatches[b] = batch;
            }
            
            // create indices
            m_indices = new NativeArray<uint>(
                6 * (m_resX - 1) * (m_resY - 1),
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory
            );

            for (var i = 0; i < m_resX - 1; i++)
            {
                for (var j = 0; j < m_resY - 1; j++)
                {
                    var id0 = (i + 0) * m_resY + (j + 0);
                    var id1 = (i + 0) * m_resY + (j + 1);
                    var id2 = (i + 1) * m_resY + (j + 1);
                    var id3 = (i + 1) * m_resY + (j + 0);

                    var index = 6 * ((i * (m_resY - 1)) + j);
                    
                    m_indices[index + 0] = (uint)id1;
                    m_indices[index + 1] = (uint)id0;
                    m_indices[index + 2] = (uint)id2;
                    m_indices[index + 3] = (uint)id2;
                    m_indices[index + 4] = (uint)id0;
                    m_indices[index + 5] = (uint)id3;
                }
            }

            m_resourcesCreated = true;
            m_dirtyFlags = ClothDirtyFlags.All;
            
            ClothManager.RegisterCloth(this);
        }

        void DisposeResources()
        {
            if (!m_resourcesCreated)
            {
                return;
            }
            
            ClothManager.DeregisterCloth(this);
            
            DisposeUtils.DisposeSafe(ref m_particles);
            DisposeUtils.DisposeSafe(ref m_constraints);
            DisposeUtils.DisposeSafe(ref m_indices);
            m_constraintBatches.Clear();

            m_resourcesCreated = false;
        }

        void OnDrawGizmosSelected()
        {
            if (!m_resourcesCreated || !m_debug)
            {
                return;
            }
            
            for (var i = 0; i < ConstraintBatchCount; i++)
            {
                var color = Color.HSVToRGB((float)i / ConstraintBatchCount, 1f, 1f);
                
                GetConstraintBatch(i, out var constraints, out _);

                for (var j = 0; j < constraints.Length; j++)
                {
                    var constraint = constraints[j];
                    var p0 = m_particles[constraint.index0];
                    var p1 = m_particles[constraint.index1];

                    if (i >= 8)
                    {
                        var offset = 0.5f * m_spacing * (i % 2 == 0 ? 1f : -1f);
                        p0.restPosition.z += offset;
                        p1.restPosition.z += offset;
                    }
                        
                    Debug.DrawLine(p0.restPosition, p1.restPosition, color);
                }
            }
        }
    }
}
