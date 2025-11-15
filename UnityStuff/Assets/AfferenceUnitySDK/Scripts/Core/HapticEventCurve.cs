using UnityEngine;

public class HapticEventCurve : AfferenceHapticEnvelope
{
    [Min(0f)] public float hapticLength = 0.5f;
    [Tooltip("If true, use Linear tangents. If false, use smooth ClampedAuto (no overshoot).")]
    public bool useLinearTangents = false;

    // Runtime end is always the declared length
    protected override float GetEndTimeSeconds() => Mathf.Max(0f, hapticLength);

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Ensure we have a curve
        if (hapticIntensity == null || hapticIntensity.length == 0)
        {
            float end = Mathf.Max(0.0001f, hapticLength > 0f ? hapticLength : 1f);
            hapticIntensity = AnimationCurve.Linear(0f, 0f, end, 0f);
            return;
        }

        // Pin the last key's time to hapticLength (authoritative)
        var keys = hapticIntensity.keys;
        int lastIdx = keys.Length - 1;

        float targetTime = Mathf.Max(0f, hapticLength);
        if (!Mathf.Approximately(keys[lastIdx].time, targetTime))
        {
            var last = keys[lastIdx];
            last.time = targetTime;
            keys[lastIdx] = last;
            hapticIntensity.keys = keys;
        }
    }
#endif
}

