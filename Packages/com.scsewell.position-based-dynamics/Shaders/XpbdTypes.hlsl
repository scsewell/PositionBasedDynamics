#ifndef XPBD_TYPES_INCLUDED
#define XPBD_TYPES_INCLUDED

struct DistanceConstraint
{
    int index0;
    int index1;
    float restLength;
    float compliance;
};

CBUFFER_START(SimulationPropertyBuffer)
uint _SubStepCount;
float _DeltaTime;
float _SubStepDeltaTime;
uint _ParticleCount;
uint _DistanceConstraintCount;
CBUFFER_END

#endif // XPBD_TYPES_INCLUDED
