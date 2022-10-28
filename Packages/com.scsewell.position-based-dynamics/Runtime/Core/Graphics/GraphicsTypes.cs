using System.Runtime.InteropServices;

using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Scsewell.PositionBasedDynamics
{
    [StructLayout(LayoutKind.Sequential)]
    struct SimulationPropertyBuffer
    {
        internal static readonly int k_size = UnsafeUtility.SizeOf<SimulationPropertyBuffer>();

        public uint _SubStepCount;
        public float _DeltaTime;
        public float _SubStepDeltaTime;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    unsafe struct ClothStaticPropertyBuffer
    {
        internal static readonly int k_size = UnsafeUtility.SizeOf<ClothStaticPropertyBuffer>();

        fixed uint _ConstraintBatchSize[4 * Constants.maxConstraintBatches];
        fixed float _ConstraintBatchCompliance[4 * Constants.maxConstraintBatches];
        public uint _ParticleCount;
        public uint _TriangleCount;
        public uint _ConstraintBatchCount;
        public float3 _BoundsMin;
        public float3 _BoundsMax;

        public void SetConstraintBatchSize(int index, uint value)
        {
            _ConstraintBatchSize[4 * index] = value;
        }
        
        public void SetConstraintBatchCompliance(int index, float value)
        {
            _ConstraintBatchCompliance[4 * index] = value;
        }
    }
    
    [StructLayout(LayoutKind.Sequential)]
    struct ClothDynamicPropertyBuffer
    {
        internal static readonly int k_size = UnsafeUtility.SizeOf<ClothDynamicPropertyBuffer>();

        public float3 _Gravity;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    readonly struct CompressedDistanceConstraint
    {
        internal static readonly int k_size = UnsafeUtility.SizeOf<CompressedDistanceConstraint>();

        readonly uint packedIndices;
        readonly float restLength;

        public CompressedDistanceConstraint(int index0, int index1, float restLength)
        {
            packedIndices =  (uint)((index0 & 0x0000FFFF) | (index1 << 16));
            this.restLength = restLength;
        }
    }
}
