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
uint _IndexCount;
CBUFFER_END

void AtomicAdd(RWStructuredBuffer<uint4> buffer, uint index, uint component, float newValue)
{
	uint v = asuint(newValue);
	uint compareValue = 0;
	uint originalValue;

	[allow_uav_condition]
	while (true)
	{
		InterlockedCompareExchange(buffer[index][component], compareValue, v, originalValue);

		if (compareValue == originalValue)
        {
			break;
		}

		compareValue = originalValue;
		v = asuint(newValue + asfloat(originalValue));
	}
}

void AtomicAdd(RWStructuredBuffer<uint4> buffer, uint index, float3 newValue, uint seed)
{
    uint s0 = seed % 3;
    uint s1 = (seed + 1) % 3;
    uint s2 = (seed + 2) % 3;

    AtomicAdd(buffer, index, s0, newValue[s0]);
    AtomicAdd(buffer, index, s1, newValue[s1]);
    AtomicAdd(buffer, index, s2, newValue[s2]);
}

#endif // XPBD_TYPES_INCLUDED
