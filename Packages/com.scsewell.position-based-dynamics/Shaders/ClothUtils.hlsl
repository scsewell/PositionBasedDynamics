#ifndef CLOTH_UTILS_INCLUDED
#define CLOTH_UTILS_INCLUDED

struct DistanceConstraint
{
    uint2 indices;
    float restLength;
};

DistanceConstraint LoadDistanceConstraint(uint index, uint batchOffset, uint batchSize)
{
    CompressedDistanceConstraint compressedConstraint;
    
    if (index < batchSize)
    {
        compressedConstraint = _DistanceConstraints[batchOffset + index];
    }
    else
    {
        compressedConstraint = (CompressedDistanceConstraint)0;
    }

    DistanceConstraint constraint;
    constraint.indices.x = compressedConstraint.packedIndices & 0x0000FFFF;
    constraint.indices.y = compressedConstraint.packedIndices >> 16;
    constraint.restLength = compressedConstraint.restLength;
    return constraint;
}

uint3 LoadTriangle(uint index)
{
    // The index buffer uses 16-bit integers, but we can only load on 4 byte boundaries.
    // We must read 4 indices and select the three that belong to the requested triangle.
    uint offset = (3 * index) / 2;
    uint2 packedIndices = index < _TriangleCount ? _MeshIndices.Load2(4 * offset) : 0;

    uint4 indices;
    indices.x = packedIndices.x & 0x0000FFFF;
    indices.y = packedIndices.x >> 16;
    indices.z = packedIndices.y & 0x0000FFFF;
    indices.w = packedIndices.y >> 16;

    return index % 2 == 0 ? indices.xyz : indices.yzw;
}

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

#endif // CLOTH_UTILS_INCLUDED
