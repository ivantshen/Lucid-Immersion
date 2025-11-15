using AfferenceEngine.src.Core.Interfaces;
using System;
using System.Linq;
using UnityEngine;

public abstract class AfferenceHaptic : MonoBehaviour
{
    [SerializeField, EncoderNamePopup] public string lateralEncoderName;
    [SerializeField, EncoderNamePopup] public string medialEncoderName;

    public bool shouldLoop;

    protected bool isPlaying;
    protected float hapticTime;      // seconds since PlayHaptic()
    public float hapticOutput;      // last computed intensity [0..1]
    private float _lastSent = -1f;  // -1 = nothing sent yet

    public void PlayHaptic()
    {
        SetEncoders();
        isPlaying = true;
        hapticTime = 0f;

        foreach (var enc in HapticManager.Instance.activeEncoders)
            enc.ClearHapticHistory();
    }

    public void StopHaptic()
    {
        if (!isPlaying) return;
        isPlaying = false;
        hapticTime = 0f;

        if (_lastSent > 0f)
        {
            SendHaptics(0f);
            _lastSent = 0f;
        }
        hapticOutput = 0f;
    }

    protected virtual void OnDisable()
    {
        if (_lastSent > 0f) SendHaptics(0f);
        isPlaying = false;
        hapticTime = 0f;
        hapticOutput = 0f;
        _lastSent = 0f;
    }

    private void Update()
    {
        if (!isPlaying) return;

        float end = Mathf.Max(0f, GetEndTimeSeconds());
        float value = Mathf.Clamp01(Evaluate(hapticTime));

        if (!Mathf.Approximately(value, _lastSent))
        {
            hapticOutput = value;
            SendHaptics(hapticOutput);     // continuous-only send
            _lastSent = hapticOutput;
        }

        hapticTime += Time.deltaTime;

        if (hapticTime > end)
        {
            if (shouldLoop && end > 0f) hapticTime = 0f;
            else StopHaptic();
        }
    }

    // Subclasses provide these for continuous curves/clips
    protected abstract float Evaluate(float timeSeconds);
    protected abstract float GetEndTimeSeconds();

    // Continuous path: send to currently-active encoders (they’ll ignore if they’re burst-only)
    protected void SendHaptics(float intensity)
    {
        if (float.IsNaN(intensity) || float.IsInfinity(intensity)) intensity = 0f;
        var val = Mathf.Clamp01(intensity);

        var targets = HapticManager.Instance?.activeEncoders;
        if (targets == null || targets.Count == 0) return;

        var now = DateTime.Now;
        foreach (var enc in targets)
            enc.AddPointandUpdate(val, now);
    }

    public void SetEncoders()
    {
        GetSelectedIndices(out int lat, out int med);

        if (HapticManager.Instance == null)
        {
            Debug.LogError("[AfferenceHaptic] HapticManager.Instance is null.");
            return;
        }

        int? lateralChannel = lat >= 0 ? 2 * (lat + 1) : (int?)null;     // 0→2,1→4,2→6...
        int? medialChannel = med >= 0 ? 2 * (med + 1) + 1 : (int?)null; // 0→3,1→5,2→7...

        HapticManager.Instance.ActivateEncoders(lateralChannel, medialChannel);
    }

    public void GetSelectedIndices(out int lateralIndex, out int medialIndex)
    {
        var names = ListEncoderNames();
        lateralIndex = string.IsNullOrEmpty(lateralEncoderName) ? -1 : Array.IndexOf(names, lateralEncoderName);
        medialIndex = string.IsNullOrEmpty(medialEncoderName) ? -1 : Array.IndexOf(names, medialEncoderName);
    }

    public static string[] ListEncoderNames()
    {
        string persistentDir = System.IO.Path.Combine(Application.persistentDataPath, "Encoders");
        string streamingDir = System.IO.Path.Combine(Application.streamingAssetsPath, "Encoders");

        string dir = System.IO.Directory.Exists(persistentDir) ? persistentDir
                    : System.IO.Directory.Exists(streamingDir) ? streamingDir
                    : null;

        if (string.IsNullOrEmpty(dir)) return Array.Empty<string>();

        return System.IO.Directory.GetFiles(dir, "*.json", System.IO.SearchOption.TopDirectoryOnly)
            .Select(System.IO.Path.GetFileNameWithoutExtension)
            .Distinct()
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
    }
}
