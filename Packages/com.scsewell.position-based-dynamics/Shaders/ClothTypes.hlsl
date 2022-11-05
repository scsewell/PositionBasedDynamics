#ifndef CLOTH_TYPES_INCLUDED
#define CLOTH_TYPES_INCLUDED

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
uint _ConstraintBatchCount;
float4 _WindVelocity;
float _AirDensity;
float _LiftCoefficient;
float _DragCoefficient;
CBUFFER_END

CBUFFER_START(ClothDynamicPropertyBuffer)
float3 _Gravity;
CBUFFER_END

#endif // CLOTH_TYPES_INCLUDED
