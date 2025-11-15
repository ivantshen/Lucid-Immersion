using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class SliderInputSync : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Slider slider;
    [SerializeField] private TMP_InputField inputField;

    [Header("Events")]
    public UnityEvent<float> OnValueChanged; // Hook SetSensitivity/SetPsych/etc. here via Inspector

    [Header("Formatting")]
    [SerializeField] private string numberFormat = "0.##"; // how the number displays

    private void Reset()
    {
        slider = GetComponentInChildren<Slider>();
        inputField = GetComponentInChildren<TMP_InputField>();
    }

    private void Start()
    {
        // Optional: make typing easier
        if (inputField) inputField.contentType = TMP_InputField.ContentType.DecimalNumber;

        // Initialize input to slider's current value without spamming events
        if (inputField) inputField.SetTextWithoutNotify(slider.value.ToString(numberFormat, CultureInfo.InvariantCulture));

        slider.onValueChanged.AddListener(HandleSliderChanged);
        inputField.onEndEdit.AddListener(HandleInputEndEdit);

        //If you want to emit the initial value to listeners, uncomment:
        //OnValueChanged?.Invoke(slider.value);
    }

    private void OnDestroy()
    {
        slider.onValueChanged.RemoveListener(HandleSliderChanged);
        inputField.onEndEdit.RemoveListener(HandleInputEndEdit);
    }

    private void HandleSliderChanged(float value)
    {
        // Keep input in sync, but don’t trigger its events
        inputField.SetTextWithoutNotify(value.ToString(numberFormat, CultureInfo.InvariantCulture));

        // Emit exactly once (slider is the source of truth)
        OnValueChanged?.Invoke(value);
    }

    private void HandleInputEndEdit(string text)
    {
        // Parse with invariant culture to always accept '.' as decimal separator
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
        {
            v = Mathf.Clamp(v, slider.minValue, slider.maxValue);

            // Update slider without notifying its listeners (prevents double invoke)
            slider.SetValueWithoutNotify(v);

            // Keep input formatted
            inputField.SetTextWithoutNotify(v.ToString(numberFormat, CultureInfo.InvariantCulture));

            // Manually emit once (since we bypassed the slider event)
            OnValueChanged?.Invoke(v);
        }
        else
        {
            // Revert to current slider value if invalid
            inputField.SetTextWithoutNotify(slider.value.ToString(numberFormat, CultureInfo.InvariantCulture));
        }
    }
}
