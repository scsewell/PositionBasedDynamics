using System;

using Unity.Collections;
using Unity.Mathematics;

using UnityEngine;

namespace Scsewell.PositionBasedDynamics
{
    class TestCloth : MonoBehaviour, ICloth
    {
        const int k_constraintTypeCount = 4;
        
        static readonly int4[] k_constraintTypeOffsets =
        {
            new int4(0, 0, 0, 1), // stretch
            new int4(0, 0, 1, 0),
            new int4(0, 0, 1, 1), // shear
            new int4(0, 1, 1, 0),
            new int4(0, 0, 0, 2), // bending
            new int4(0, 0, 2, 0),
        };

        [SerializeField]
        Material m_material;
        [SerializeField]
        Vector2Int m_resolution = new Vector2Int(44, 44);
        [SerializeField]
        Bounds m_bounds = new Bounds(Vector3.zero, Vector3.one);
        [SerializeField]
        float m_spacing = 0.01f;
        [SerializeField]
        bool m_hang = true;
        [SerializeField, Range(0, 1f)]
        float m_stretchCompliance = 0f;
        [SerializeField, Range(0, 1f)]
        float m_shearCompliance = 0.001f;
        [SerializeField, Range(0, 1f)]
        float m_bendingCompliance = 1f;
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
        int[] m_constraintBatchSizes;
        int m_indexCount;

        /// <inheritdoc />
        public string Name => name;

        /// <inheritdoc />
        public int ParticleCount => m_particles.Length;

        /// <inheritdoc />
        public int IndexCount => m_indexCount;

        /// <inheritdoc />
        public int ConstraintBatchCount => m_constraintBatchSizes.Length;

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

        void OnDrawGizmosSelected()
        {
            if (m_resourcesCreated && m_debug)
            {
                for (var i = 0; i < ConstraintBatchCount; i++)
                {
                    var color = Color.HSVToRGB((float)i / ConstraintBatchCount, 1f, 1f);
                
                    GetConstraintBatch(i, out var constraints, out _);

                    for (var j = 0; j < constraints.Length; j++)
                    {
                        var constraint = constraints[j];
                        var p0 = m_particles[constraint.index0];
                        var p1 = m_particles[constraint.index1];
                    
                        Debug.DrawLine(p0.restPosition, p1.restPosition, color);
                    }
                }
            }
        }

        void CreateResources()
        {
            DisposeResources();

            m_particles = new NativeArray<ClothParticle>(
                m_resolution.x * m_resolution.y,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory
            );
            
            for (var i = 0; i < m_resolution.x; i++)
            {
                for (var j = 0; j < m_resolution.y; j++)
                {
                    var id = i * m_resolution.y + j;

                    var position = new Vector3(
                        m_spacing * (-0.5f * m_resolution.x + i),
                        m_spacing * (-0.5f * m_resolution.y + j),
                        0f
                    );
                    position += 0.001f * UnityEngine.Random.insideUnitSphere;
                    
                    m_particles[id] = new ClothParticle
                    {
                        restPosition = position,
                        inverseMass = (m_hang && j == (m_resolution.y - 1) && (i == 0 || i == m_resolution.x - 1)) ? 0f : 1f,
                    };
                }
            }

            var batchCount = 2 * k_constraintTypeCount;
            m_constraintBatchSizes = new int[batchCount];
            
            m_constraints = new NativeArray<ClothConstraint>(
                batchCount * m_particles.Length,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory
            );

            for (var type = 0; type < k_constraintTypeCount; type++)
            {
                for (var i = 0; i < m_resolution.x; i++)
                {
                    for (var j = 0; j < m_resolution.y; j++)
                    {
                        var offset = k_constraintTypeOffsets[type] + new int4(i, j, i, j);

                        if (offset.x >= m_resolution.x ||
                            offset.y >= m_resolution.y ||
                            offset.z >= m_resolution.x ||
                            offset.w >= m_resolution.y)
                        {
                            continue;
                        }
                        
                        var id0 = offset.x * m_resolution.y + offset.y;
                        var id1 = offset.z * m_resolution.y + offset.w;

                        var batchIndex = (2 * type) + type switch
                        {
                            0 => j % 2,
                            _ => i % 2,
                        };
                        var batchSize = m_constraintBatchSizes[batchIndex]++;
                        var constraintIndex = (m_particles.Length * batchIndex) + batchSize;
                        
                        m_constraints[constraintIndex] = new ClothConstraint
                        {
                            index0 = id0,
                            index1 = id1,
                            restLength = math.distance(
                                m_particles[id0].restPosition,
                                m_particles[id1].restPosition
                            ),
                        };
                    }
                }
            }
            
            m_indices = new NativeArray<uint>(
                m_resolution.x * m_resolution.y * 6,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory
            );

            m_indexCount = 0;
            
            for (var i = 0; i < m_resolution.x - 1; i++)
            {
                for (var j = 0; j < m_resolution.y - 1; j++)
                {
                    var id = i * m_resolution.y + j;
                    
                    m_indices[m_indexCount++] = (uint)(id + 1);
                    m_indices[m_indexCount++] = (uint)(id);
                    m_indices[m_indexCount++] = (uint)(id + 1 + m_resolution.y);
                    m_indices[m_indexCount++] = (uint)(id + 1 + m_resolution.y);
                    m_indices[m_indexCount++] = (uint)(id);
                    m_indices[m_indexCount++] = (uint)(id + m_resolution.y);
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
            
            m_particles.Dispose();
            m_constraints.Dispose();
            m_indices.Dispose();

            m_resourcesCreated = false;
        }

        /// <inheritdoc />
        public NativeSlice<ClothParticle> GetParticles()
        {
            return m_particles.Slice();
        }

        /// <inheritdoc />
        public NativeSlice<uint> GetIndices()
        {
            return m_indices.Slice(0, m_indexCount);
        }

        /// <inheritdoc />
        public int GetConstraintBatchSize(int batchIndex)
        {
            return m_constraintBatchSizes[batchIndex];
        }

        /// <inheritdoc />
        public void GetConstraintBatch(int batchIndex, out NativeSlice<ClothConstraint> constraints, out float compliance)
        {
            if (batchIndex < 4)
            {
                compliance = Mathf.Pow(m_stretchCompliance, 4);
            }
            else if (batchIndex < 8)
            {
                compliance = Mathf.Pow(m_shearCompliance, 4);
            }
            else if (batchIndex < 12)
            {
                compliance = Mathf.Pow(m_bendingCompliance, 4);
            }
            else
            {
                compliance = 0f;
            }

            var batchOffset = m_particles.Length * batchIndex;
            var batchSize = m_constraintBatchSizes[batchIndex];
            
            constraints = m_constraints.Slice(batchOffset, batchSize);
        }
    }
}
