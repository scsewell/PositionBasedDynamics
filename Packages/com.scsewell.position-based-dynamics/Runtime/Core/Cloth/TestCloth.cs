using System.Collections.Generic;
using System.Linq;

using Unity.Collections;
using Unity.Mathematics;

using UnityEngine;
using UnityEngine.Rendering;

namespace Scsewell.PositionBasedDynamics.Core
{
    public class TestCloth : Cloth
    {
        [SerializeField]
        Material m_material;
        [SerializeField]
        Vector2Int m_resolution = new Vector2Int(30, 200);
        [SerializeField]
        bool m_hang = false;
        [SerializeField, Range(0, 10f)]
        float m_stretchCompliance = 0f;
        [SerializeField, Range(0, 10f)]
        float m_shearCompliance = 0.001f;
        [SerializeField, Range(0, 10f)]
        float m_bendingCompliance = 1f;

        Mesh m_mesh;
        MeshFilter m_filter;
        MeshRenderer m_renderer;
        
        /// <inheritdoc />
        protected override void OnEnable()
        {
            base.OnEnable();

            m_mesh = new Mesh
            {
                name = name,
            };
            m_mesh.MarkDynamic();

            m_filter = GetComponent<MeshFilter>();

            if (m_filter == null)
            {
                m_filter = gameObject.AddComponent<MeshFilter>();
            }
            
            m_filter.sharedMesh = m_mesh;
            
            m_renderer = GetComponent<MeshRenderer>();

            if (m_renderer == null)
            {
                m_renderer = gameObject.AddComponent<MeshRenderer>();
            }

            m_renderer.sharedMaterial = m_material;
        }

        /// <inheritdoc />
        protected override void OnDisable()
        {
            base.OnDisable();

            if (m_mesh != null)
            {
                Destroy(m_mesh);
                m_mesh = null;
            }
        }

        /// <inheritdoc />
        protected override void GetCloth(out NativeSlice<ClothParticle> particles, out NativeSlice<Constraint> constraints)
        {
            particles = new NativeArray<ClothParticle>(
                m_resolution.x * m_resolution.y,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory
            ).Slice();
            
            for (var i = 0; i < m_resolution.x; i++)
            {
                for (var j = 0; j < m_resolution.y; j++)
                {
                    var id = i * m_resolution.y + j;

                    var position = new Vector3(
                        -m_resolution.x * Spacing * 0.5f + i * Spacing,
                        0.2f + j * Spacing,
                        0f
                    );
                    position += 0.001f * UnityEngine.Random.insideUnitSphere;
                    
                    particles[id] = new ClothParticle
                    {
                        restPosition = position,
                        inverseMass = (m_hang && j == (m_resolution.y - 1) && (i == 0 || i == m_resolution.x - 1)) ? 0f : 1f,
                    };
                }
            }

            var constraintTypeCount = 6;
            
            var offsets = new NativeArray<int4>(
                constraintTypeCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory
            )
            {
                [0] = new int4(0, 0, 0, 1),
                [1] = new int4(0, 0, 1, 0),
                [2] = new int4(0, 0, 1, 1),
                [3] = new int4(0, 1, 1, 0),
                [4] = new int4(0, 0, 0, 2),
                [5] = new int4(0, 0, 2, 0),
            };
            
            var compliances = new NativeArray<float>(
                constraintTypeCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory
            )
            {
                [0] = m_stretchCompliance,
                [1] = m_stretchCompliance,
                [2] = m_shearCompliance,
                [3] = m_shearCompliance,
                [4] = m_bendingCompliance,
                [5] = m_bendingCompliance,
            };
            
            var tempConstraints = new NativeArray<Constraint>(
                particles.Length * constraintTypeCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory
            );

            var num = 0;

            for (var constType = 0; constType < constraintTypeCount; constType++)
            {
                for (var i = 0; i < m_resolution.x; i++)
                {
                    for (var j = 0; j < m_resolution.y; j++)
                    {
                        var offset = offsets[constType] + new int4(i, j, i, j);
                        
                        if (offset.x < m_resolution.x && offset.y < m_resolution.y && offset.z < m_resolution.x && offset.w < m_resolution.y)
                        {
                            var id0 = offset.x * m_resolution.y + offset.y;
                            var id1 = offset.z * m_resolution.y + offset.w;
                            
                            tempConstraints[num++] = new Constraint
                            {
                                index0 = id0,
                                index1 = id1,
                                restLength = math.distance(particles[id0].restPosition, particles[id1].restPosition),
                                compliance = compliances[constType],
                            };
                        }
                    }
                }
            }
            
            constraints = tempConstraints.Slice(0, num);
        }

        /// <inheritdoc />
        protected override void ClothUpdated(NativeSlice<float3> positions)
        {
            return;
            
            if (m_mesh != null)
            {
                m_mesh.Clear();

                var vertices = new Vector3[positions.Length];

                for (var i = 0; i < positions.Length; i++)
                {
                    vertices[i] = positions[i];
                }
                
                var indices = new List<int>();
            
                for (var i = 0; i < m_resolution.x; i++)
                {
                    for (var j = 0; j < m_resolution.y; j++)
                    {
                        var id = i * m_resolution.y + j;
                    
                        if (i < m_resolution.x - 1 && j < m_resolution.y - 1)
                        {
                            indices.Add(id + 1);
                            indices.Add(id);
                            indices.Add(id + 1 + m_resolution.y);
                            indices.Add(id + 1 + m_resolution.y);
                            indices.Add(id);
                            indices.Add(id + m_resolution.y);
                        }
                    }
                }
                
                m_mesh.vertices = vertices;

                m_mesh.indexFormat = IndexFormat.UInt32;
                m_mesh.triangles = indices.ToArray();

                m_mesh.RecalculateNormals();
                m_mesh.RecalculateBounds();
                m_mesh.UploadMeshData(false);
            }
        }
    }
}
