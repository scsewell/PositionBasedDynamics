using System.Runtime.InteropServices;

using Unity.Collections.LowLevel.Unsafe;

namespace Scsewell.PositionBasedDynamics
{
    [StructLayout(LayoutKind.Sequential)]
    public struct DistanceConstraint
    {
        internal static readonly int k_size = UnsafeUtility.SizeOf<DistanceConstraint>();

        public int index0;
        public int index1;
        public float restLength;
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
        public uint _IndexCount;
    }
}
