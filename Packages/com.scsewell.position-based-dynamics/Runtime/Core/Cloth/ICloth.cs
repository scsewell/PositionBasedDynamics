using System;

using Unity.Collections;
using Unity.Mathematics;

using UnityEngine;

namespace Scsewell.PositionBasedDynamics
{
    /// <summary>
    /// Flags used to indicate how a cloth instance has changed since the last simulation step.
    /// </summary>
    [Flags]
    public enum ClothDirtyFlags
    {
        /// <summary>
        /// The configuration has not changed.
        /// </summary>
        None = 0,
        
        /// <summary>
        /// The particles were changed.
        /// </summary>
        Particles = ClothState.DirtyFlags.StaticData,

        /// <summary>
        /// The particle constraints were changed.
        /// </summary>
        Constraints = ClothState.DirtyFlags.StaticData,

        /// <summary>
        /// The simulation bounds were changed.
        /// </summary>
        Bounds = ClothState.DirtyFlags.StaticData,

        /// <summary>
        /// The gravity acceleration was changed.
        /// </summary>
        Gravity = ClothState.DirtyFlags.DynamicProperties,

        /// <summary>
        /// Force a complete refresh of all data.
        /// </summary>
        All = ~0,
    }

    /// <summary>
    /// An interface for cloth that can be simulated by registering the cloth to the <see cref="ClothManager"/>.
    /// </summary>
    public interface ICloth
    {
        /// <summary>
        /// The flags indicating if the cloth was changed.
        /// </summary>
        ClothDirtyFlags DirtyFlags { get; protected set; }

        /// <summary>
        /// The name of the cloth instance.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// The number of particles of the cloth.
        /// </summary>
        /// <remarks>
        /// This number of particles should not exceed <see cref="Constants.maxParticlesPerCloth"/>.
        /// </remarks>
        int ParticleCount { get; }
        
        /// <summary>
        /// The number of mesh indices of the cloth.
        /// </summary>
        int IndexCount { get; }

        /// <summary>
        /// The number of constraint batches needed for this cloth instance.
        /// </summary>
        /// <remarks>
        /// The number of batches should not exceed <see cref="Constants.maxConstraintBatches"/>.
        /// If the number of batches is larger, the extra constraint batches will be ignored.
        /// </remarks>
        int ConstraintBatchCount { get; }

        /// <summary>
        /// The bounds of the cloth simulation.
        /// </summary>
        Bounds Bounds { get; }

        /// <summary>
        /// The gravity to apply to the cloth.
        /// </summary>
        float3 Gravity { get; }

        /// <summary>
        /// The transform of the cloth.
        /// </summary>
        Matrix4x4 Transform { get; }

        /// <summary>
        /// The material used to render the cloth.
        /// </summary>
        Material Material { get; }

        /// <summary>
        /// Gets the particles of the cloth.
        /// </summary>
        /// <remarks>
        /// The particles are not validated automatically, it is up to the cloth
        /// implementation to ensure the data is valid.
        /// </remarks>
        /// <returns>A slice containing the cloth particles.</returns>
        NativeSlice<ClothParticle> GetParticles();
        
        /// <summary>
        /// Gets the mesh indices of the cloth.
        /// </summary>
        /// <remarks>
        /// The indices are not validated automatically, it is up to the cloth
        /// implementation to ensure the data is valid.
        /// </remarks>
        /// <returns>A slice containing the mesh indices.</returns>
        NativeSlice<uint> GetIndices();

        /// <summary>
        /// Gets the number of constraints in the specified batch.
        /// </summary>
        /// <param name="batchIndex">The index of the constraint batch.</param>
        /// <returns>The number of constraints in the batch.</returns>
        int GetConstraintBatchSize(int batchIndex);

        /// <summary>
        /// Gets a batch of constraints affecting the cloth particles.
        /// </summary>
        /// <remarks>
        /// No two constraints in the same batch may constrain the same particle. Use a
        /// graph coloring algorithm or knowledge of the mesh topology to create the batches
        /// efficiently. Performance may be improved by using as few batches as possible.
        /// The constraints are not validated automatically, it is up to the cloth
        /// implementation to ensure the data is valid.
        /// </remarks>
        /// <param name="batchIndex">The index of the constraint batch.</param>
        /// <param name="constraints">Returns the constraints in the batch.</param>
        /// <param name="compliance">
        /// Returns the inverse of the constraint stiffness. A value of zero makes the constraints
        /// perfectly stiff, larger values increase the constraints' flexibility.
        /// </param>
        void GetConstraintBatch(int batchIndex, out NativeSlice<ClothConstraint> constraints, out float compliance);

        /// <summary>
        /// Resets the dirty flags of the cloth.
        /// </summary>
        /// <remarks>
        /// This is called by <see cref="ClothManager"/> once it has refreshed any data marked
        /// as dirty by the <see cref="ClothDirtyFlags"/>.
        /// </remarks>
        void ClearDirtyFlags()
        {
            DirtyFlags = ClothDirtyFlags.None;
        }
    }
}
