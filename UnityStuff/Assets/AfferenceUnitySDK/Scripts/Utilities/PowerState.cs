using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PowerState : MonoBehaviour
{
    private TMP_Text buttonText;
    public string State1;
    public string State2;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        buttonText = GetComponentInChildren<TMP_Text>();
        buttonText.text = State1;
    }

    // Update is called once per frame
    void Update()
    {
        if (HapticManager.Instance == null)
        {
            Debug.Log("No Haptic Manager");
            return;
        }

        buttonText.text = HapticManager.Instance.stimActive ? State1 : State2;
    }
}
