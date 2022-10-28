using System;

namespace Scsewell.PositionBasedDynamics
{
    /// <summary>
    /// A constraint that attempts to maintain a set distance between a pair of particles. 
    /// </summary>
    [Serializable]
    public struct ClothConstraint
    {
        /// <summary>
        /// The index of the first constrained particle.
        /// </summary>
        public int index0;
        
        /// <summary>
        /// The index of the second constrained particle.
        /// </summary>
        public int index1;
        
        /// <summary>
        /// The target distance between the constrained particles.
        /// </summary>
        public float restLength;
    }
}
