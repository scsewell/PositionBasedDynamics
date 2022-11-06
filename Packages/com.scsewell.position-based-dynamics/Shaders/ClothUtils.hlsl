#ifndef CLOTH_UTILS_INCLUDED
#define CLOTH_UTILS_INCLUDED

struct NeighbourFan
{
	uint indices[MAX_TRIS_PER_PARTICLE];
};

struct DistanceConstraint
{
    uint2 indices;
    float restLength;
};

NeighbourFan LoadNeighbourFan(uint index)
{
	uint3 packedIndices = _NeighbourFanIndices[index];

	NeighbourFan fan;
	fan.indices[0] = packedIndices.x & 0xFFFF;
	fan.indices[1] = packedIndices.x >> 16;
	fan.indices[2] = packedIndices.y & 0xFFFF;
	fan.indices[3] = packedIndices.y >> 16;
	fan.indices[4] = packedIndices.z & 0xFFFF;
	fan.indices[5] = packedIndices.z >> 16;
	return fan;
}

DistanceConstraint LoadDistanceConstraint(uint index, uint batchOffset)
{
    CompressedDistanceConstraint compressedConstraint = _DistanceConstraints[batchOffset + index];

    DistanceConstraint constraint;
    constraint.indices.x = compressedConstraint.packedIndices & 0xFFFF;
    constraint.indices.y = compressedConstraint.packedIndices >> 16;
    constraint.restLength = compressedConstraint.restLength;
    return constraint;
}

#endif // CLOTH_UTILS_INCLUDED
