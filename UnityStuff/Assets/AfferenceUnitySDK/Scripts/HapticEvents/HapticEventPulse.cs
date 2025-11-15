using AfferenceEngine.src.Core.Interfaces;
using AfferenceEngine.src.Core.Other; // BurstModel
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class HapticEventPulse : AfferenceHaptic
{
    [Header("Burst Settings")]
    public bool useCustomBurst = false;
    public Vector2[] customBurst = Array.Empty<Vector2>();
    public float initialDelayMs = 0f;
    public BurstModel burstModel = BurstModel.Tap;
    public string burstNodeHint = null; // optional preferred BurstTrain node name

    public new void PlayHaptic()   // shadow base to run burst instead of continuous loop
    {
        SetEncoders();

        if (!HapticManager.Instance.stimActive) { return; }

        var encs = GetBurstEncoders();
        if (encs.Count == 0)
        {
            Debug.LogWarning("[HapticEventPulse] Active encoders are not burst-capable (no BurstTrain found).");
            return;
        }

        var now = DateTimeOffset.Now.AddMilliseconds(1); // ensure strictly increasing time

        if (useCustomBurst && customBurst != null && customBurst.Length > 0)
        {
            string pattern = BuildBurstPatternString(customBurst, initialDelayMs);
            foreach (var enc in encs)
            {
                var nodeName = ResolveBurstTrainName(enc, burstNodeHint);
                if (string.IsNullOrEmpty(nodeName)) continue;

                enc.ClearHapticHistory();
                enc.StartBurst(nodeName, now, pattern);
            }
        }
        else
        {
            var model = burstModel; // snapshot
            foreach (var enc in encs)
            {
                var nodeName = ResolveBurstTrainName(enc, burstNodeHint);
                if (string.IsNullOrEmpty(nodeName)) continue;

                enc.ClearHapticHistory();
                enc.StartBurst(nodeName, now, model);
            }
        }
    }

    // --- local helpers (no base dependency on bursts) ---

    private static bool IsBurstCapable(IQualityEncoder enc)
    {
        var cached = HapticManager.Instance?.GetBurstTrainNames(enc.ID);
        return cached != null && cached.Count > 0;
    }

    private static List<IQualityEncoder> GetBurstEncoders()
    {
        var encs = HapticManager.Instance?.activeEncoders ?? new List<IQualityEncoder>();
        return encs.Where(IsBurstCapable).ToList();
    }

    private static string ResolveBurstTrainName(IQualityEncoder enc, string hint)
    {
        var cached = HapticManager.Instance?.GetBurstTrainNames(enc.ID);
        if (cached != null && cached.Count > 0)
        {
            if (!string.IsNullOrWhiteSpace(hint))
            {
                var hit = cached.FirstOrDefault(n => string.Equals(n, hint, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(hit)) return hit;
            }
            return cached.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).First();
        }
        return string.IsNullOrWhiteSpace(hint) ? "Burst" : hint; // conventional fallback
    }

    private static string BuildBurstPatternString(Vector2[] points, float initialDelayMs)
    {
        if (points == null || points.Length == 0) return "[]";
        var parts = new List<string>(points.Length);
        for (int i = 0; i < points.Length; i++)
        {
            float d = points[i].x + (i == 0 ? Mathf.Max(0f, initialDelayMs) : 0f);
            float a = points[i].y;
            parts.Add("(" + d.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                            a.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")");
        }
        return "[" + string.Join("", parts) + "]";
    }

    // not used for bursts; keep base abstract contract happy
    protected override float Evaluate(float timeSeconds) => 0f;
    protected override float GetEndTimeSeconds() => 0f;
}
