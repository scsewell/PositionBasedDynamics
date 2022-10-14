using UnityEngine;

namespace Scsewell.PositionBasedDynamics
{
    [CreateAssetMenu(fileName = "New XpbdResources", menuName = "Xpbd/Resources", order = 410)]
    class XpbdResources : ScriptableObject
    {
        [SerializeField]
        ComputeShader m_resetParticles;
        [SerializeField]
        ComputeShader m_integrate;
        [SerializeField]
        ComputeShader m_solveDistanceConstraints;
        [SerializeField]
        ComputeShader m_updateVelocities;

        public ComputeShader ResetParticles => m_resetParticles;
        
        public ComputeShader Integrate => m_integrate;
    
        public ComputeShader SolveDistanceConstraints => m_solveDistanceConstraints;
    
        public ComputeShader UpdateVelocities => m_updateVelocities;
    }
}
