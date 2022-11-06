#ifndef CLOTH_UNIFORMS_INCLUDED
#define CLOTH_UNIFORMS_INCLUDED

StructuredBuffer<float> _InverseMasses;
RWStructuredBuffer<uint3> _NeighbourFanIndices;
StructuredBuffer<CompressedDistanceConstraint> _DistanceConstraints;

RWStructuredBuffer<float4> _CurrentPositions;
RWStructuredBuffer<float4> _PreviousPositions;

RWByteAddressBuffer _MeshPositions;
RWByteAddressBuffer _MeshNormals;

#endif // CLOTH_UNIFORMS_INCLUDED
