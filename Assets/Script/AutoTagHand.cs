using UnityEngine;

public class AutoTagHand : MonoBehaviour
{
    void Start()
    {
        // Auto tag "Hand" when object actived
        gameObject.tag = "Hand";
        Debug.Log("Auto tag Hand: " + gameObject.name);
    }
}