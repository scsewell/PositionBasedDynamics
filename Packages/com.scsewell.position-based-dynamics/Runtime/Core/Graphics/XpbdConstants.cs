using UnityEngine;

namespace Scsewell.PositionBasedDynamics
{
    /// <summary>
    /// A class containing constant values.
    /// </summary>
    static class Constants
    {
    }

    /// <summary>
    /// A class containing shader property IDs.
    /// </summary>
    static class Properties
    {
        public static class ResetParticles
        {
            public static readonly int _BaseIndex = Shader.PropertyToID("_BaseIndex");
            public static readonly int _Count = Shader.PropertyToID("_Count");
            
            public static readonly int _RestPositions = Shader.PropertyToID("_RestPositions");
            public static readonly int _OldPositions = Shader.PropertyToID("_OldPositions");
            public static readonly int _OldPrevPositions = Shader.PropertyToID("_OldPrevPositions");
            public static readonly int _OldVelocities = Shader.PropertyToID("_OldVelocities");
            public static readonly int _Positions = Shader.PropertyToID("_Positions");
            public static readonly int _PrevPositions = Shader.PropertyToID("_PrevPositions");
            public static readonly int _Velocities = Shader.PropertyToID("_Velocities");
        }
        
        public static class Integrate
        {
            public static readonly int _ConstantBuffer = Shader.PropertyToID("SimulationPropertyBuffer");

            public static readonly int _ParticleGroupIndices = Shader.PropertyToID("_ParticleGroupIndices");
            public static readonly int _InverseMasses = Shader.PropertyToID("_InverseMasses");
            public static readonly int _Gravity = Shader.PropertyToID("_Gravity");
            public static readonly int _Positions = Shader.PropertyToID("_Positions");
            public static readonly int _Velocities = Shader.PropertyToID("_Velocities");
        }
        
        public static class SolveDistanceConstraints
        {
            public static readonly int _ConstantBuffer = Shader.PropertyToID("SimulationPropertyBuffer");
            
            public static readonly int _ConstraintCount = Shader.PropertyToID("_ConstraintCount");
            
            public static readonly int _Constraints = Shader.PropertyToID("_Constraints");
            public static readonly int _InverseMasses = Shader.PropertyToID("_InverseMasses");
            public static readonly int _Positions = Shader.PropertyToID("_Positions");
        }

        public static class UpdateVelocities
        {
            public static readonly int _ConstantBuffer = Shader.PropertyToID("SimulationPropertyBuffer");

            public static readonly int _Positions = Shader.PropertyToID("_Positions");
            public static readonly int _PrevPositions = Shader.PropertyToID("_PrevPositions");
            public static readonly int _Velocities = Shader.PropertyToID("_Velocities");
        }

        public static class UpdateMesh
        {
            public static readonly int _ConstantBuffer = Shader.PropertyToID("SimulationPropertyBuffer");

            public static readonly int _TriangleIndices = Shader.PropertyToID("_TriangleIndices");
            public static readonly int _Positions = Shader.PropertyToID("_Positions");
            public static readonly int _Normals = Shader.PropertyToID("_Normals");
        }
    }

    /// <summary>
    /// A class containing shader keywords.
    /// </summary>
    static class Keywords
    {
    }

    /// <summary>
    /// A class containing compute shader kernel names.
    /// </summary>
    static class Kernels
    {
        public static class ResetParticles
        {
            public const string k_reset = "Reset";
            public const string k_copy = "Copy";
        }
        
        public static class Integrate
        {
            public const string k_main = "CSMain";
        }
        
        public static class SolveDistanceConstraints
        {
            public const string k_main = "CSMain";
        }

        public static class UpdateVelocities
        {
            public const string k_main = "CSMain";
        }

        public static class UpdateMesh
        {
            public const string k_sumTriangleNormals = "SumTriangleNormals";
            public const string k_computeVertexNormals = "ComputeVertexNormals";
        }
    }
}
