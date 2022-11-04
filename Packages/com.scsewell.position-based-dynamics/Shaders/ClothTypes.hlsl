#ifndef CLOTH_TYPES_INCLUDED
#define CLOTH_TYPES_INCLUDED

struct GroupData
{
    uint particlesInIndex;
    uint particlesUniqueIndex;
    uint particlesOutIndex;
    uint particlesEndIndex;

    uint sharedInIndex;
    uint sharedOutIndex;

    uint trianglesStartIndex;
    uint trianglesEndIndex;
};

struct CompressedDistanceConstraint
{
    uint packedIndices;
    float restLength;
};

CBUFFER_START(SimulationPropertyBuffer)
uint _SubStepCount;
float _DeltaTime;
float _SubStepDeltaTime;
CBUFFER_END

CBUFFER_START(ClothStaticPropertyBuffer)
uint4 _ConstraintBatchData[MAX_CONSTRAINT_BATCHES];
uint _ParticleCount;
uint _TriangleCount;
uint _ConstraintBatchCount;
uint _ThreadGroupCount;
float4 _BoundsMin;
float4 _BoundsMax;
CBUFFER_END

CBUFFER_START(ClothDynamicPropertyBuffer)
float3 _Gravity;
CBUFFER_END

#endif // CLOTH_TYPES_INCLUDED
