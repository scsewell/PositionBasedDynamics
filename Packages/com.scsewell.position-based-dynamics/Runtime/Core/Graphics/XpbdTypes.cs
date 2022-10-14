using System;
using System.Runtime.InteropServices;

using Unity.Collections.LowLevel.Unsafe;

namespace Scsewell.PositionBasedDynamics
{
    /// <summary>
    /// A constraint that attempts to maintain a set distance between a pair of particles. 
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct DistanceConstraint
    {
        internal static readonly int k_size = UnsafeUtility.SizeOf<DistanceConstraint>();

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
        
        /// <summary>
        /// The inverse of the constraint stiffness.
        /// </summary>
        /// <remarks>
        /// A value of zero makes the constraint perfectly stiff, larger values
        /// increase the constraint's flexibility.
        /// </remarks>
        public float compliance;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    struct SimulationPropertyBuffer
    {
        internal static readonly int k_size = UnsafeUtility.SizeOf<SimulationPropertyBuffer>();

        public uint _SubStepCount;
        public float _DeltaTime;
        public float _SubStepDeltaTime;
        public uint _ParticleCount;
        public uint _DistanceConstraintCount;
    }
}
