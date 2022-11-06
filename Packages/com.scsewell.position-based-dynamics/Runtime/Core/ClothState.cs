using System;
using System.Collections.Generic;
using System.Text;

using Unity.Collections;
using Unity.Mathematics;

using UnityEngine;
using UnityEngine.Rendering;

namespace Scsewell.PositionBasedDynamics
{
    class ClothState : IDisposable
    {
        [Flags]
        internal enum DirtyFlags
        {
            None = 0,
            StaticData = 1 << 0,
            DynamicProperties = 1 << 1,
            All = ~0,
        }
        
        static readonly VertexAttributeDescriptor[] k_vertexAttributes =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 1),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, 2),
        };

        // command buffers
        CommandBuffer m_cmdBuffer;

        // constant buffers
        NativeArray<ClothStaticPropertyBuffer> m_staticProperties;
        NativeArray<ClothDynamicPropertyBuffer> m_dynamicProperties;
        
        GraphicsBuffer m_staticPropertyBuffer;
        GraphicsBuffer m_dynamicPropertyBuffer;
        
        // compute buffers
        GraphicsBuffer m_inverseMassesBuffer;
        GraphicsBuffer m_neighbourhoodFansBuffer;
        GraphicsBuffer m_distanceConstraintsBuffer;
        GraphicsBuffer m_currentPositionsBuffer;
        GraphicsBuffer m_previousPositionsBuffer;
        
        // the mesh used for rendering
        Mesh m_mesh;
        GraphicsBuffer m_meshPositionBuffer;
        GraphicsBuffer m_meshNormalBuffer;

        DirtyFlags m_dirtyFlags = DirtyFlags.All;
        int m_particleCount;
        int m_indexCount;

        public ICloth Cloth { get; }

        public ClothState(ICloth cloth)
        {
            Cloth = cloth;
            
            // create command buffer
            m_cmdBuffer = new CommandBuffer
            {
                name = $"{nameof(ClothState)}_{Cloth.Name}_CommandBuffer",
            };

            // create constant buffers
            m_staticProperties = new NativeArray<ClothStaticPropertyBuffer>(
                1,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory
            );
            m_dynamicProperties = new NativeArray<ClothDynamicPropertyBuffer>(
                1,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory
            );

            m_staticPropertyBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Constant,
                GraphicsBuffer.UsageFlags.None,
                m_staticProperties.Length,
                ClothStaticPropertyBuffer.k_size
            )
            {
                name = $"{nameof(ClothState)}_{Cloth.Name}_{nameof(ClothStaticPropertyBuffer)}",
            };
            m_dynamicPropertyBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Constant,
                GraphicsBuffer.UsageFlags.LockBufferForWrite,
                m_dynamicProperties.Length,
                ClothDynamicPropertyBuffer.k_size
            )
            {
                name = $"{nameof(ClothState)}_{Cloth.Name}_{nameof(ClothDynamicPropertyBuffer)}",
            };
            
            // create rendering mesh
            m_mesh = new Mesh
            {
                name = $"{nameof(ClothState)}_{Cloth.Name}_Mesh",
            };
        }
        
        public void Dispose()
        {
            // dispose command buffers
            DisposeUtils.DisposeSafe(ref m_cmdBuffer);

            // dispose constant buffers
            DisposeUtils.DisposeSafe(ref m_staticProperties);
            DisposeUtils.DisposeSafe(ref m_dynamicProperties);
            
            DisposeUtils.DisposeSafe(ref m_staticPropertyBuffer);
            DisposeUtils.DisposeSafe(ref m_dynamicPropertyBuffer);

            // dispose compute buffers
            DisposeUtils.DisposeSafe(ref m_inverseMassesBuffer);
            DisposeUtils.DisposeSafe(ref m_neighbourhoodFansBuffer);
            DisposeUtils.DisposeSafe(ref m_distanceConstraintsBuffer);
            DisposeUtils.DisposeSafe(ref m_currentPositionsBuffer);
            DisposeUtils.DisposeSafe(ref m_previousPositionsBuffer);

            // dispose mesh
            DisposeUtils.DestroySafe(ref m_mesh);
            DisposeUtils.DisposeSafe(ref m_meshPositionBuffer);
            DisposeUtils.DisposeSafe(ref m_meshNormalBuffer);
            
            // clear managed state
            m_dirtyFlags = DirtyFlags.All;
            m_particleCount = 0;
            m_indexCount = 0;
        }
        
        public void Update(CommandBuffer cmdBuffer)
        {
            using var _ = new ProfilerScope($"{nameof(ClothState)}.{nameof(Update)}()");

            m_dirtyFlags = (DirtyFlags)Cloth.DirtyFlags;

            if (m_dirtyFlags == DirtyFlags.None)
            {
                return;
            }
            
            // update any buffers whose contents is invalidated
            if (m_dirtyFlags.Intersects(DirtyFlags.StaticData))
            {
                UpdateBuffers();
            }
            if (m_dirtyFlags.Intersects(DirtyFlags.DynamicProperties))
            {
                var buffer = m_dynamicPropertyBuffer.LockBufferForWrite<ClothDynamicPropertyBuffer>(0, 1);
                
                buffer[0] = new ClothDynamicPropertyBuffer
                {
                    _Gravity = Cloth.Gravity,
                    _WindVelocity = Cloth.Wind,
                    _AirDensity = Cloth.AirDensity,
                    _LiftCoefficient = Cloth.LiftCoefficient,
                    _DragCoefficient = Cloth.DragCoefficient,
                };
                
                m_dynamicPropertyBuffer.UnlockBufferAfterWrite<ClothDynamicPropertyBuffer>(1);
                
                /*
                var gravity = new NativeArray<float4>(
                    s_cloths.Count,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory
                );
            
                cmdBuffer.SetBufferData(s_gravityBuffer, gravity);
                */
            }

            Cloth.ClearDirtyFlags();
        }

        public void Simulate()
        {
            using var _ = new ProfilerScope($"{nameof(ClothState)}.{nameof(Simulate)}()");

            Graphics.ExecuteCommandBuffer(m_cmdBuffer);
        }

        public void Render()
        {
            using var _ = new ProfilerScope($"{nameof(ClothState)}.{nameof(Render)}()");

            Graphics.DrawMesh(m_mesh, Cloth.Transform, Cloth.Material, 0);
        }

        unsafe void UpdateBuffers()
        {
            using var _ = new ProfilerScope($"{nameof(ClothState)}.{nameof(UpdateBuffers)}()");

            // clear all existing state 
            m_cmdBuffer.Clear();

            DisposeUtils.DisposeSafe(ref m_distanceConstraintsBuffer);
            DisposeUtils.DisposeSafe(ref m_inverseMassesBuffer);
            DisposeUtils.DisposeSafe(ref m_neighbourhoodFansBuffer);
            DisposeUtils.DisposeSafe(ref m_currentPositionsBuffer);
            DisposeUtils.DisposeSafe(ref m_previousPositionsBuffer);
        
            m_mesh.Clear();
            DisposeUtils.DisposeSafe(ref m_meshPositionBuffer);
            DisposeUtils.DisposeSafe(ref m_meshNormalBuffer);
            
            // find the required buffer sizes
            m_particleCount = Mathf.Max(Cloth.ParticleCount, 0);
            m_indexCount = Mathf.Max(Cloth.IndexCount, 0);

            if (m_particleCount == 0)
            {
                Debug.LogWarning($"Cloth \"{Cloth.Name}\" has no particles.");
                return;
            }
            if (m_particleCount > Constants.maxParticlesPerCloth)
            {
                Debug.LogError($"Cloth \"{Cloth.Name}\" has {m_particleCount} particles, exceeding the limit of {Constants.maxParticlesPerCloth}.");
                m_particleCount = Constants.maxParticlesPerCloth;
            }
            
            var constraintBatchCount = Mathf.Max(Cloth.ConstraintBatchCount, 0);
            
            if (constraintBatchCount == 0)
            {
                Debug.LogError($"Cloth \"{Cloth.Name}\" has no constraint batches.");
                return;
            }
            if (constraintBatchCount > Constants.maxConstraintBatches)
            {
                Debug.LogError($"Cloth \"{Cloth.Name}\" has {constraintBatchCount} constraint batches, exceeding the limit of {Constants.maxConstraintBatches}.");
                constraintBatchCount = Constants.maxConstraintBatches;
            }

            var distanceConstraintCount = 0;

            for (var i = 0; i < constraintBatchCount; i++)
            {
                var batchSize = Mathf.Max(Cloth.GetConstraintBatchSize(i), 0);
                
                if (batchSize == 0)
                {
                    Debug.LogWarning($"Cloth \"{Cloth.Name}\" has no constraints in batch {i}.");
                }
                
                distanceConstraintCount += batchSize;
            }
            
            if (distanceConstraintCount == 0)
            {
                Debug.LogError($"Cloth \"{Cloth.Name}\" has no constraints.");
                return;
            }

            var threadGroupCount = ClothManager.s_clothKernel.GetThreadGroupCount(
                m_particleCount,
                Constants.particlesPerThread
            );

            // get the buffer data
            var inverseMasses = new NativeArray<float>(
                m_particleCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory
            );
            var neighbourhoodTris = new NativeMultiHashMap<int, (ushort, ushort)>(
                m_particleCount * Constants.maxTrisPerParticle,
                Allocator.Temp
            );
            var neighbourhoodFans = new NativeArray<ushort>(
                m_particleCount * Constants.maxTrisPerParticle,
                Allocator.Temp
            );
            var restPositions = new NativeArray<float4>(
                m_particleCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory
            );
            var uvs = new NativeArray<float2>(
                m_particleCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory
            );
            
            var distanceConstraints = new NativeArray<CompressedDistanceConstraint>(
                distanceConstraintCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory
            );
            
            var indices = new NativeArray<ushort>(
                m_indexCount + 1, // pad the array so we don't overrun reading the last index
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory
            );

            var neighbourhood = stackalloc (ushort, ushort)[Constants.maxTrisPerParticle];

            var clothParticles = Cloth.GetParticles();
            var clothIndices = Cloth.GetIndices();
            
            for (var i = 0; i < m_indexCount; i++)
            {
                indices[i] = (ushort)clothIndices[i];
            }
            for (var i = 0; i < m_indexCount; i += 3)
            {
                var i0 = (ushort)clothIndices[i];
                var i1 = (ushort)clothIndices[i + 1];
                var i2 = (ushort)clothIndices[i + 2];
                
                neighbourhoodTris.Add(i0, (i1, i2));
                neighbourhoodTris.Add(i1, (i2, i0));
                neighbourhoodTris.Add(i2, (i0, i1));
            }

            for (var i = 0; i < m_particleCount; i++)
            {
                var particle = clothParticles[i];

                inverseMasses[i] = particle.inverseMass;
                restPositions[i] = new float4(particle.restPosition, 0f);
                uvs[i] = particle.uv;

                var neighbourhoodSize = 0;
                
                foreach (var tri in neighbourhoodTris.GetValuesForKey(i))
                {
                    neighbourhood[neighbourhoodSize++] = tri;
                }

                SortNeighbourhood(neighbourhood, neighbourhoodSize);

                var fanOffset = i * Constants.maxTrisPerParticle;

                for (var j = 0; j < neighbourhoodSize; j++)
                {
                    neighbourhoodFans[fanOffset + j] = (ushort)(neighbourhood[j].Item1 | Constants.FilledFanBit);
                }
                if (neighbourhoodSize > 0 && neighbourhoodSize < Constants.maxTrisPerParticle)
                {
                    neighbourhoodFans[fanOffset + neighbourhoodSize] = neighbourhood[neighbourhoodSize - 1].Item2;
                }
            }
            
            neighbourhoodTris.Dispose();

            var staticProperties = new ClothStaticPropertyBuffer
            {
                _ParticleCount = (uint)m_particleCount,
                _ConstraintBatchCount = (uint)constraintBatchCount,
            };
            
            var currentConstraint = 0;

            for (var i = 0; i < constraintBatchCount; i++)
            {
                Cloth.GetConstraintBatch(i, out var constraints, out var compliance);

                staticProperties.SetConstraintData(i, (uint)currentConstraint, (uint)constraints.Length, compliance);
                
                for (var j = 0; j < constraints.Length; j++)
                {
                    var constraint = constraints[j];

                    distanceConstraints[currentConstraint++] = new CompressedDistanceConstraint(
                        constraint.index0,
                        constraint.index1,
                        constraint.restLength
                    );
                }
            }

            m_staticProperties[0] = staticProperties;
            m_staticPropertyBuffer.SetData(m_staticProperties);
            
            // create compute buffers
            m_inverseMassesBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                GraphicsBuffer.UsageFlags.None,
                m_particleCount,
                sizeof(uint)
            )
            {
                name = $"{nameof(ClothState)}_{Cloth.Name}_InverseMasses",
            };
            m_neighbourhoodFansBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                GraphicsBuffer.UsageFlags.None,
                m_particleCount,
                3 * sizeof(uint)
            )
            {
                name = $"{nameof(ClothState)}_{Cloth.Name}_NeighbourhoodFans",
            };
            m_currentPositionsBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                GraphicsBuffer.UsageFlags.None,
                m_particleCount,
                4 * sizeof(float)
            )
            {
                name = $"{nameof(ClothState)}_{Cloth.Name}_CurrentPositions",
            };
            m_previousPositionsBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                GraphicsBuffer.UsageFlags.None,
                m_particleCount,
                4 * sizeof(float)
            )
            {
                name = $"{nameof(ClothState)}_{Cloth.Name}_PreviousPositions",
            };
            m_distanceConstraintsBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                GraphicsBuffer.UsageFlags.None,
                distanceConstraintCount,
                CompressedDistanceConstraint.k_size
            )
            {
                name = $"{nameof(ClothState)}_{Cloth.Name}_DistanceConstraints",
            };
            
            m_inverseMassesBuffer.SetData(inverseMasses);
            m_neighbourhoodFansBuffer.SetData(neighbourhoodFans);
            m_currentPositionsBuffer.SetData(restPositions);
            m_previousPositionsBuffer.SetData(restPositions);
            m_distanceConstraintsBuffer.SetData(distanceConstraints);

            inverseMasses.Dispose();
            neighbourhoodFans.Dispose();
            restPositions.Dispose();
            distanceConstraints.Dispose();
            
            // configure the rendering mesh
            m_mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
            m_mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;
            m_mesh.SetVertexBufferParams(m_particleCount, k_vertexAttributes);
            m_mesh.SetIndexBufferParams(indices.Length, IndexFormat.UInt16);

            var subMesh = new SubMeshDescriptor
            {
                bounds = Cloth.Bounds,
                topology = MeshTopology.Triangles,
                vertexCount = m_particleCount,
                indexCount = m_indexCount,
            };

            m_meshPositionBuffer = m_mesh.GetVertexBuffer(0);
            m_meshPositionBuffer.name = $"{nameof(ClothState)}_{Cloth.Name}_MeshPositions";
            
            m_meshNormalBuffer = m_mesh.GetVertexBuffer(1);
            m_meshNormalBuffer.name = $"{nameof(ClothState)}_{Cloth.Name}_MeshNormals";

            m_mesh.SetVertexBufferData(uvs, 0, 0, m_particleCount, 2);
            m_mesh.SetIndexBufferData(indices, 0, 0, indices.Length, MeshUpdateFlags.DontValidateIndices);
            m_mesh.SetSubMesh(0, subMesh, MeshUpdateFlags.DontRecalculateBounds);
            m_mesh.bounds = Cloth.Bounds;
            
            uvs.Dispose();
            indices.Dispose();

            // configure the command buffer
            m_cmdBuffer.SetComputeConstantBufferParam(
                ClothManager.s_clothShader,
                Properties.Cloth._SimulationPropertyBuffer,
                ClothManager.s_simulationConstantBuffer,
                0,
                SimulationPropertyBuffer.k_size
            );
            m_cmdBuffer.SetComputeConstantBufferParam(
                ClothManager.s_clothShader,
                Properties.Cloth._ClothStaticPropertyBuffer,
                m_staticPropertyBuffer,
                0,
                ClothStaticPropertyBuffer.k_size
            );
            m_cmdBuffer.SetComputeConstantBufferParam(
                ClothManager.s_clothShader,
                Properties.Cloth._ClothDynamicPropertyBuffer,
                m_dynamicPropertyBuffer,
                0,
                ClothDynamicPropertyBuffer.k_size
            );
            
            m_cmdBuffer.SetComputeBufferParam(
                ClothManager.s_clothShader,
                ClothManager.s_clothKernel.kernelID,
                Properties.Cloth._DistanceConstraints,
                m_distanceConstraintsBuffer
            );
            m_cmdBuffer.SetComputeBufferParam(
                ClothManager.s_clothShader,
                ClothManager.s_clothKernel.kernelID,
                Properties.Cloth._InverseMasses,
                 m_inverseMassesBuffer
            );
            m_cmdBuffer.SetComputeBufferParam(
                ClothManager.s_clothShader,
                ClothManager.s_clothKernel.kernelID,
                Properties.Cloth._NeighbourFanIndices,
                m_neighbourhoodFansBuffer
            );
            m_cmdBuffer.SetComputeBufferParam(
                ClothManager.s_clothShader,
                ClothManager.s_clothKernel.kernelID,
                Properties.Cloth._CurrentPositions,
                m_currentPositionsBuffer
            );
            m_cmdBuffer.SetComputeBufferParam(
                ClothManager.s_clothShader,
                ClothManager.s_clothKernel.kernelID,
                Properties.Cloth._PreviousPositions,
                m_previousPositionsBuffer
            );
            m_cmdBuffer.SetComputeBufferParam(
                ClothManager.s_clothShader,
                ClothManager.s_clothKernel.kernelID,
                Properties.Cloth._MeshPositions,
                m_meshPositionBuffer
            );
            m_cmdBuffer.SetComputeBufferParam(
                ClothManager.s_clothShader,
                ClothManager.s_clothKernel.kernelID,
                Properties.Cloth._MeshNormals,
                m_meshNormalBuffer
            );

            m_cmdBuffer.DispatchCompute(
                ClothManager.s_clothShader,
                ClothManager.s_clothKernel.kernelID, 
                threadGroupCount, 1, 1
            );
        }

        static unsafe void SortNeighbourhood((ushort, ushort)* neighbourhood, int count)
        {
            var start = 0;
            var end = count;

            while (start < end - 1)
            {
                var item = neighbourhood[start];
                var swapIndex = start + 1;
                var found = false;
                
                for (var i = swapIndex; i < end; i++)
                {
                    var current = neighbourhood[i];

                    if (item.Item2 != current.Item1)
                    {
                        continue;
                    }
                    
                    if (i != swapIndex)
                    {
                        (neighbourhood[swapIndex], neighbourhood[i]) = (neighbourhood[i], neighbourhood[swapIndex]);
                    }
                    
                    found = true;
                    break;
                }
                
                if (found)
                {
                    start++;
                    continue;
                }
                
                var temp = stackalloc (ushort, ushort)[swapIndex];
                var remaining = end - swapIndex;
                
                for (var i = 0; i < swapIndex; i++)
                {
                    temp[i] = neighbourhood[i];
                }
                for (var i = swapIndex; i < end; i++)
                {
                    neighbourhood[i - swapIndex] = neighbourhood[i];
                }
                for (var i = 0; i < swapIndex; i++)
                {
                    neighbourhood[remaining + i] = temp[i];
                }

                start = 0;
                end = remaining;
            }
        }
    }
}
