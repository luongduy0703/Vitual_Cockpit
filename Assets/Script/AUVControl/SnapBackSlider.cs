using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class SnapBackSlider : MonoBehaviour, IPointerUpHandler
{
    public Slider slider;
    public int neutral = 1500;

    void Reset()
    {
        slider = GetComponent<Slider>();
    }

    void Awake()
    {
        if (slider == null) slider = GetComponent<Slider>();
        if (slider != null)
        {
            if (slider.minValue > neutral || slider.maxValue < neutral)
            {
                Debug.LogWarning($"SnapBackSlider: neutral {neutral} over range [{slider.minValue},{slider.maxValue}]");
            }
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (slider != null)
        {
            slider.value = neutral;
        }
    }
}