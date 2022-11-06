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

        fixed uint _ConstraintBatchData[4 * Constants.maxConstraintBatches];
        public uint _ParticleCount;
        public uint _ConstraintBatchCount;

        public void SetConstraintData(int index, uint batchOffset, uint batchSize, float compliance)
        {
            _ConstraintBatchData[(4 * index) + 0] = batchOffset;
            _ConstraintBatchData[(4 * index) + 1] = batchSize;
            _ConstraintBatchData[(4 * index) + 2] = *(uint*)&compliance;
        }
    }
    
    [StructLayout(LayoutKind.Sequential)]
    struct ClothDynamicPropertyBuffer
    {
        internal static readonly int k_size = UnsafeUtility.SizeOf<ClothDynamicPropertyBuffer>();

        public float3 _Gravity;
        public float _AirDensity;
        public float3 _WindVelocity;
        public float _LiftCoefficient;
        public float _DragCoefficient;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    readonly struct CompressedDistanceConstraint
    {
        internal static readonly int k_size = UnsafeUtility.SizeOf<CompressedDistanceConstraint>();

        readonly uint packedIndices;
        readonly float restLength;

        public CompressedDistanceConstraint(int index0, int index1, float restLength)
        {
            packedIndices =  (uint)((index0 & 0xFFFF) | (index1 << 16));
            this.restLength = restLength;
        }
    }
}
