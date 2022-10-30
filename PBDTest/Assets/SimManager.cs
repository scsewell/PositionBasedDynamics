using Scsewell.PositionBasedDynamics;

using UnityEngine;
using UnityEngine.UI;

class SimManager : MonoBehaviour
{
    [SerializeField]
    Toggle m_fixedStepsToggle;
    [SerializeField]
    Slider m_subStepsPerFrameValue;
    [SerializeField]
    Text m_subStepsPerFrameText;
    [SerializeField]
    Slider m_subStepsPerSecondValue;
    [SerializeField]
    Text m_subStepsPerSecondText;

    void OnEnable()
    {
        m_subStepsPerSecondValue.value = ClothManager.SubStepsPerSecond;
    }

    void Update()
    {
        if (m_fixedStepsToggle.isOn)
        {
            ClothManager.UpdateMode = ClothUpdateMode.Manual;
            ClothManager.SubStepsPerSecond = 144;
            ClothManager.Simulate(Mathf.RoundToInt(m_subStepsPerFrameValue.value) / ClothManager.SubStepsPerSecond);
        }
        else
        {
            ClothManager.UpdateMode = ClothUpdateMode.Automatic;
            ClothManager.SubStepsPerSecond = Mathf.RoundToInt(m_subStepsPerSecondValue.value);
        }
        
        m_subStepsPerFrameText.text = m_subStepsPerFrameValue.value.ToString();
        m_subStepsPerSecondText.text = m_subStepsPerSecondValue.value.ToString();
    }
}
