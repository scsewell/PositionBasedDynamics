using Unity.Mathematics;

namespace Scsewell.PositionBasedDynamics
{
    /// <summary>
    /// A struct that defines a cloth particle that can be simulated.
    /// </summary>
    public struct ClothParticle
    {
        /// <summary>
        /// The default position of the particle.
        /// </summary>
        public float3 restPosition;
        
        /// <summary>
        /// The inverse of the mass of the particle.
        /// </summary>
        public float inverseMass;
    }
}
