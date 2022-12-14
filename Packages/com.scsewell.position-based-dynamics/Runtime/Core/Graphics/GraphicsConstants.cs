using UnityEngine;

namespace Scsewell.PositionBasedDynamics
{
    /// <summary>
    /// A class containing constant values.
    /// </summary>
    public static class Constants
    {
        /// <summary>
        /// The maximum number of particles per cloth instance.
        /// </summary>
        public const int maxParticlesPerCloth = 2048;
        
        /// <summary>
        /// The maximum number of distance batches per cloth instance.
        /// </summary>
        public const int maxConstraintBatches = 12;
        
        /// <summary>
        /// The maximum number of triangles in the neighbourhood of a particle.
        /// </summary>
        public const int maxTrisPerParticle = 6;

        /// <summary>
        /// The number of particles processed per compute thread.
        /// </summary>
        internal const int particlesPerThread = 2;
        
        /// <summary>
        /// The value used to represent a fan neighbourhood triangle.
        /// </summary>
        internal const ushort FilledFanBit = 0x8000;
    }

    /// <summary>
    /// A class containing shader property IDs.
    /// </summary>
    static class Properties
    {
        public static class Cloth
        {
            public static readonly int _SimulationPropertyBuffer = Shader.PropertyToID("SimulationPropertyBuffer");
            public static readonly int _ClothStaticPropertyBuffer = Shader.PropertyToID("ClothStaticPropertyBuffer");
            public static readonly int _ClothDynamicPropertyBuffer = Shader.PropertyToID("ClothDynamicPropertyBuffer");

            public static readonly int _InverseMasses = Shader.PropertyToID("_InverseMasses");
            public static readonly int _NeighbourFanIndices = Shader.PropertyToID("_NeighbourFanIndices");
            public static readonly int _DistanceConstraints = Shader.PropertyToID("_DistanceConstraints");
            
            public static readonly int _CurrentPositions = Shader.PropertyToID("_CurrentPositions");
            public static readonly int _PreviousPositions = Shader.PropertyToID("_PreviousPositions");
            
            public static readonly int _MeshPositions = Shader.PropertyToID("_MeshPositions");
            public static readonly int _MeshNormals = Shader.PropertyToID("_MeshNormals");
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
        public static class Cloth
        {
            public const string main = "CSMain";
        }
    }
}
