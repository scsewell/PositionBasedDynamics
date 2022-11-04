#ifndef CLOTH_UNIFORMS_INCLUDED
#define CLOTH_UNIFORMS_INCLUDED

StructuredBuffer<CompressedDistanceConstraint> _DistanceConstraints;

RWStructuredBuffer<CompressedPosition> _CurrentPositions;
RWStructuredBuffer<CompressedPosition> _PreviousPositions;
RWStructuredBuffer<uint4> _Normals;

RWByteAddressBuffer _MeshPositions;
RWByteAddressBuffer _MeshNormals;
ByteAddressBuffer _MeshIndices;

#endif // CLOTH_UNIFORMS_INCLUDED
