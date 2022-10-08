namespace Scsewell.PositionBasedDynamics.Core
{
    /// <summary>
    /// A constraint that attempts to maintain a set distance between a pair of particles. 
    /// </summary>
    public struct Constraint
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
        /// The target distance between the particles.
        /// </summary>
        public float restLength;
        
        /// <summary>
        /// The strength of the constraint. This value represents how flexible this constraint is.
        /// </summary>
        public float compliance;
    }
}
