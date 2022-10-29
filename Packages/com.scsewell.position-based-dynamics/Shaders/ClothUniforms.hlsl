#ifndef CLOTH_UNIFORMS_INCLUDED
#define CLOTH_UNIFORMS_INCLUDED

StructuredBuffer<float> _InverseMasses;
StructuredBuffer<CompressedDistanceConstraint> _DistanceConstraints;

RWStructuredBuffer<float4> _CurrentPositions;
RWStructuredBuffer<float4> _PreviousPositions;
RWStructuredBuffer<uint4> _Normals;

RWByteAddressBuffer _MeshPositions;
RWByteAddressBuffer _MeshNormals;
ByteAddressBuffer _MeshIndices;

#endif // CLOTH_UNIFORMS_INCLUDED
