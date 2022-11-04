#ifndef CLOTH_UTILS_INCLUDED
#define CLOTH_UTILS_INCLUDED

struct DistanceConstraint
{
    uint2 indices;
    float restLength;
};

DistanceConstraint LoadDistanceConstraint(uint index, uint batchOffset)
{
    CompressedDistanceConstraint compressedConstraint = _DistanceConstraints[batchOffset + index];
    
    DistanceConstraint constraint;
    constraint.indices.x = compressedConstraint.packedIndices & 0x0000FFFF;
    constraint.indices.y = compressedConstraint.packedIndices >> 16;
    constraint.restLength = compressedConstraint.restLength;
    return constraint;
}

CompressedPosition CompressPosition(float3 position, uint bit)
{
    float3 relativePosition = (position - _BoundsMin) / (_BoundsMax - _BoundsMin);
    uint3 packed = uint3((saturate(relativePosition) * 0x1FFFFF) + 0.5);

    CompressedPosition compressed;
    compressed.packedData.x = packed.x | (packed.y << 21);
    compressed.packedData.y = (packed.y >> 11) | (packed.z << 10) | (bit << 31);
    return compressed;
}

CompressedPosition CompressPosition(float3 position)
{
    return CompressPosition(position, 0);
}

float3 DecompressPosition(CompressedPosition position, out uint bit)
{
    uint3 packed;
    packed.x = position.packedData.x & 0x1FFFFF;
    packed.y = ((position.packedData.x >> 21) | (position.packedData.y << 11)) & 0x1FFFFF;
    packed.z = (position.packedData.y >> 10) & 0x1FFFFF;
    bit      = position.packedData.y >> 31;

    float3 relativePosition = float3(packed) / 0x1FFFFF;
    return lerp(_BoundsMin, _BoundsMax, relativePosition);
}

float3 DecompressPosition(CompressedPosition compressedPosition)
{
    uint bit;
    return DecompressPosition(compressedPosition, bit);
}

uint3 LoadTriangle(uint index)
{
    // The index buffer uses 16-bit integers, but we can only load on 4 byte boundaries.
    // We must read 4 indices and select the three that belong to the requested triangle.
    uint offset = (3 * index) / 2;
    uint2 packedIndices = _MeshIndices.Load2(4 * offset);

    uint4 indices;
    indices.x = packedIndices.x & 0xFFFF;
    indices.y = packedIndices.x >> 16;
    indices.z = packedIndices.y & 0xFFFF;
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
