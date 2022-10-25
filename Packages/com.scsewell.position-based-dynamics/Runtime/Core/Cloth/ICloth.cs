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
        Particles = XpbdManager.DirtyFlags.UpdateBuffers,

        /// <summary>
        /// The particle constraints were changed.
        /// </summary>
        Constraints = XpbdManager.DirtyFlags.UpdateBuffers,

        /// <summary>
        /// The simulation bounds were changed.
        /// </summary>
        Bounds = XpbdManager.DirtyFlags.UpdateBuffers,

        /// <summary>
        /// The gravity acceleration was changed.
        /// </summary>
        Gravity = XpbdManager.DirtyFlags.Gravity,

        /// <summary>
        /// Force a complete refresh of all data.
        /// </summary>
        All = ~0,
    }

    /// <summary>
    /// An interface for cloth that can be simulated by registering the cloth to the <see cref="XpbdManager"/>.
    /// </summary>
    public interface ICloth
    {
        /// <summary>
        /// The flags indicating if the cloth was changed.
        /// </summary>
        ClothDirtyFlags DirtyFlags { get; protected set; }

        /// <summary>
        /// The number of cloth particles.
        /// </summary>
        int ParticleCount { get; }
        
        /// <summary>
        /// The number of groups of independent constraints.
        /// </summary>
        int ConstraintGroupCount { get; }

        /// <summary>
        /// The number of mesh indices for the cloth.
        /// </summary>
        int IndexCount { get; }

        /// <summary>
        /// The bounds of the simulation.
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
        /// Gets the cloth particles to be simulated.
        /// </summary>
        /// <remarks>
        /// The particles are not validated automatically, it is up to the cloth
        /// implementation to ensure the data is valid.
        /// </remarks>
        /// <returns>A slice containing the cloth particles.</returns>
        NativeSlice<ClothParticle> GetParticles();
        
        /// <summary>
        /// Gets the number of constraints in the specified group.
        /// </summary>
        /// <param name="groupIndex">The index of the constraint group.</param>
        /// <returns>The number of constraints in the group.</returns>
        int GetConstraintGroupSize(int groupIndex);

        /// <summary>
        /// Gets a group of constraints affecting the cloth particles.
        /// </summary>
        /// <remarks>
        /// No two constraints in the same group may constrain the same particle. Use a
        /// graph coloring algorithm or knowledge of the mesh topology to create the groups
        /// efficiently. Performance is improved by using as few groups as possible.
        /// The constraints are not validated automatically, it is up to the cloth
        /// implementation to ensure the data is valid.
        /// </remarks>
        /// <param name="groupIndex">The index of the constraint group.</param>
        /// <returns>A slice containing the constraints in the group.</returns>
        NativeSlice<ClothConstraint> GetConstraintGroup(int groupIndex);

        /// <summary>
        /// Gets the mesh indices for the cloth.
        /// </summary>
        /// <remarks>
        /// The indices are not validated automatically, it is up to the cloth
        /// implementation to ensure the data is valid.
        /// </remarks>
        /// <returns>A slice containing the mesh indices.</returns>
        NativeSlice<uint> GetIndices();

        /// <summary>
        /// Resets the dirty flags of the cloth.
        /// </summary>
        /// <remarks>
        /// This is called by <see cref="XpbdManager"/> once it has refreshed any data marked
        /// as dirty by the <see cref="ClothDirtyFlags"/>.
        /// </remarks>
        void ClearDirtyFlags()
        {
            DirtyFlags = ClothDirtyFlags.None;
        }
    }
}
