using UnityEngine;

public class HapticEventClip : AfferenceHapticEnvelope
{
    [Tooltip("Assigned .haptic or .ahap file (editor generates the curve).")]
    public Object hapticClip;

    [Tooltip("Optional gain multiplier applied when generating the curve in the editor.")]
    public float intensityGain = 1f;

    [Tooltip("Invert the clip values when generating the curve in the editor.")]
    public bool invertClip = false;

    // Runtime does not need Start/Update — base class drives playback.
    // The curve (hapticIntensity) is filled in by the custom editor.
}
