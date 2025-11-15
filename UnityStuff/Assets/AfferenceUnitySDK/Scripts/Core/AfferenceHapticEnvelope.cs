using UnityEngine;

public class AfferenceHapticEnvelope : AfferenceHaptic
{
    [Tooltip("Envelope intensity over time (seconds). Last key time = end time.")]
    public AnimationCurve hapticIntensity = AnimationCurve.Linear(0, 1, 1, 1);

    protected override float Evaluate(float t)
    {
        if (hapticIntensity == null || hapticIntensity.length == 0) return 0f;
        float end = hapticIntensity[hapticIntensity.length - 1].time;
        return hapticIntensity.Evaluate(Mathf.Min(t, end));
    }

    protected override float GetEndTimeSeconds()
    {
        if (hapticIntensity == null || hapticIntensity.length == 0) return 0f;
        return Mathf.Max(0f, hapticIntensity[hapticIntensity.length - 1].time);
    }
}
