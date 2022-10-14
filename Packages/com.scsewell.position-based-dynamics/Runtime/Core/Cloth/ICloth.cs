using System;

using Unity.Collections;
using Unity.Mathematics;

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
        /// The number of constraints affecting the cloth.
        /// </summary>
        int ConstraintCount { get; }

        /// <summary>
        /// The gravity to apply to the cloth.
        /// </summary>
        float3 Gravity { get; }

        // todo graph coloring, optionally provided, and calucalted if not
        
        /// <summary>
        /// Gets the cloth particles to be simulated.
        /// </summary>
        /// <remarks>
        /// The particles are not validated automatically, it is up to the cloth
        /// implementation to ensure the particles are valid.
        /// </remarks>
        /// <returns>A slice containing the particles for the cloth instance.</returns>
        NativeSlice<ClothParticle> GetParticles();
        
        /// <summary>
        /// Gets the constraints affecting the cloth particles.
        /// </summary>
        /// <remarks>
        /// The constraints are not validated automatically, it is up to the cloth
        /// implementation to ensure the constraints are valid.
        /// </remarks>
        /// <returns>A slice containing the constraints for the cloth instance.</returns>
        NativeSlice<DistanceConstraint> GetConstraints();

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
