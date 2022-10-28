using UnityEngine;

namespace Scsewell.PositionBasedDynamics
{
    [CreateAssetMenu(fileName = "New ClothResources", menuName = "Cloth/Resources", order = 410)]
    class ClothResources : ScriptableObject
    {
        [SerializeField]
        ComputeShader m_cloth;

        public ComputeShader Cloth => m_cloth;
    }
}
