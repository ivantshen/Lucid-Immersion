// Newtonsoft for robust JSON (Unity package: com.unity.nuget.newtonsoft-json)
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class HapticCurveGenerator : Editor
{
    // Accumulators
    private static readonly List<float> hapticTimeStamps = new List<float>();
    private static readonly List<float> hapticIntensities = new List<float>();

    // .haptic-only (kept if you later need frequency)
    private static readonly List<float> frequencyTimeStamps = new List<float>();
    private static readonly List<float> frequencyIntensities = new List<float>();

    // Tuning
    private const float EPS = 0.001f;                 // step-edge epsilon (s)
    private const float TRANSIENT_WIDTH_AHAP = 0.004f;

    public static AnimationCurve GenerateCurve(UnityEngine.Object hapticFile, float multiplier, bool invert)
    {
        // reset
        hapticTimeStamps.Clear();
        hapticIntensities.Clear();
        frequencyTimeStamps.Clear();
        frequencyIntensities.Clear();

        string filePath = AssetDatabase.GetAssetPath(hapticFile);
        string json = File.ReadAllText(filePath);
        string ext = Path.GetExtension(filePath).ToLowerInvariant();

        bool isAhap = false;
        bool isHaps = false;

        if (ext == ".haptic")
        {
            var hapticData = JsonUtility.FromJson<HapticData>(json);
            if (hapticData?.signals?.continuous?.envelopes == null)
            {
                Debug.LogError("Failed to parse .haptic JSON.");
                return new AnimationCurve();
            }

            // amplitude envelope — leave as-provided
            if (hapticData.signals.continuous.envelopes.amplitude != null)
            {
                foreach (var env in hapticData.signals.continuous.envelopes.amplitude)
                {
                    hapticTimeStamps.Add(env.time);
                    hapticIntensities.Add(env.amplitude);
                }
            }

            // frequency envelope (unused here, but preserved)
            if (hapticData.signals.continuous.envelopes.frequency != null)
            {
                foreach (var f in hapticData.signals.continuous.envelopes.frequency)
                {
                    frequencyTimeStamps.Add(f.time);
                    frequencyIntensities.Add(f.frequency);
                }
            }
        }
        else if (ext == ".ahap")
        {
            isAhap = true;

            var ahap = JsonUtility.FromJson<AHAPData>(json);
            if (ahap?.Pattern == null)
            {
                Debug.LogError("Failed to parse .ahap JSON or missing Pattern.");
                return new AnimationCurve();
            }

            // Partition events
            var continuousEvents = ahap.Pattern
                .Where(p => p.Event != null && p.Event.EventType == "HapticContinuous")
                .Select(p => p.Event).ToList();

            var transientEvents = ahap.Pattern
                .Where(p => p.Event != null && p.Event.EventType == "HapticTransient")
                .Select(p => p.Event).ToList();

            // Intensity curves (ignore sharpness)
            var intensityCurves = ahap.Pattern
                .Where(p => p.ParameterCurve != null && p.ParameterCurve.ParameterID == "HapticIntensityControl")
                .Select(p => p.ParameterCurve).ToList();

            // Precompute curve metadata
            var curveInfos = new List<CurveInfo>();
            foreach (var c in intensityCurves)
            {
                var pts = c.ParameterCurveControlPoints ?? new List<ControlPoint>();
                if (pts.Count == 0) continue;

                pts = pts.OrderBy(p => p.Time).ToList();
                float absStart = pts[0].Time + c.Time;
                float absEnd = pts[pts.Count - 1].Time + c.Time;
                var absTimes = pts.Select(p => Round3(p.Time + c.Time)).ToList();

                curveInfos.Add(new CurveInfo
                {
                    curve = c,
                    absStart = absStart,
                    absEnd = absEnd,
                    absTimes = absTimes,
                    localSorted = pts
                });
            }

            // ---- Build one global set of sample times ----
            var sampleTimes = new SortedSet<float> { 0f };

            foreach (var ev in continuousEvents)
            {
                float s = Round3(ev.Time);
                float e = Round3(ev.Time + ev.EventDuration);
                if (e < s) { float tmp = s; s = e; e = tmp; }

                sampleTimes.Add(Mathf.Max(0, s - EPS));
                sampleTimes.Add(s);
                sampleTimes.Add(e);
                sampleTimes.Add(e + EPS);
            }

            foreach (var info in curveInfos)
                foreach (var tAbs in info.absTimes) sampleTimes.Add(tAbs);

            foreach (var tev in transientEvents)
            {
                float t0 = Round3(tev.Time);
                float t1 = Round3(tev.Time + TRANSIENT_WIDTH_AHAP);
                sampleTimes.Add(Mathf.Max(0, t0 - EPS));
                sampleTimes.Add(t0);
                sampleTimes.Add(t1);
                sampleTimes.Add(t1 + EPS);
            }

            // ---- Evaluate the whole clip at each sample time ----
            foreach (var t in sampleTimes)
            {
                float contVal = 0f;

                // contributions from ALL continuous events covering t
                foreach (var ev in continuousEvents)
                {
                    float s = ev.Time;
                    float e = ev.Time + ev.EventDuration;
                    if (t < s - 1e-6f || t > e + 1e-6f) continue;

                    float eventIntensity = GetEventParam(ev, "HapticIntensity", 0f);

                    float curveAtT = 0f;
                    bool anyCurveActive = false;
                    foreach (var info in curveInfos)
                    {
                        if (t + 1e-6f < info.absStart || t - 1e-6f > info.absEnd) continue;
                        anyCurveActive = true;
                        curveAtT = Mathf.Max(curveAtT, SampleCurve(info, t));
                    }

                    float evContribution = anyCurveActive ? curveAtT : eventIntensity;
                    contVal = Mathf.Max(contVal, evContribution);
                }

                // transients
                float transVal = 0f;
                foreach (var tev in transientEvents)
                {
                    float t0 = tev.Time;
                    float t1 = tev.Time + TRANSIENT_WIDTH_AHAP;
                    if (t + 1e-6f < t0 || t - 1e-6f > t1) continue;
                    transVal = Mathf.Max(transVal, GetEventParam(tev, "HapticIntensity", 1f));
                }

                float v = Mathf.Clamp01(Mathf.Max(contVal, transVal));
                hapticTimeStamps.Add(t);
                hapticIntensities.Add(v);
            }
        }
        else if (ext == ".haps")
        {
            isHaps = true;

            // -------- Interhaptics HAPS (matches your files) --------
            var root = JToken.Parse(json) as JObject;
            if (root == null)
            {
                Debug.LogError("Invalid .haps JSON.");
                return new AnimationCurve();
            }

            float rootGain = GetFloat(root, "m_gain", 1f);

            var vib = root["m_vibration"] as JObject;
            if (vib == null)
            {
                Debug.LogError(".haps missing m_vibration.");
                return new AnimationCurve();
            }
            float vibGain = GetFloat(vib, "m_gain", 1f);

            var melodiesTok = vib["m_melodies"] as JArray;
            if (melodiesTok == null)
            {
                Debug.LogError(".haps missing m_vibration.m_melodies.");
                return new AnimationCurve();
            }

            // Global sample set and accumulators
            var sampleTimes = new SortedSet<float> { 0f };
            var curveLists = new List<(float start, float end, float scale, List<(float t, float v)> keys)>();
            var transientRects = new List<(float start, float end, float intensity)>();

            foreach (var melTok in melodiesTok.OfType<JObject>())
            {
                float melGain = GetFloat(melTok, "m_gain", 1f);
                float melScale = rootGain * vibGain * melGain;

                var notes = melTok["m_notes"] as JArray;
                if (notes == null) continue;

                foreach (var note in notes.OfType<JObject>())
                {
                    float nStart = GetFloat(note, "m_startingPoint", 0f);
                    float nLen = GetFloat(note, "m_length", 0f);
                    float nEnd = nStart + nLen;
                    float nGain = GetFloat(note, "m_gain", 1f);
                    float scale = melScale * nGain;

                    var eff = note["m_hapticEffect"] as JObject;
                    var amp = eff != null ? eff["m_amplitudeModulation"] as JObject : null;
                    var kfs = amp != null ? amp["m_keyframes"] as JArray : null;

                    if (kfs != null && kfs.Count > 0)
                    {
                        // TAKE ALL KEYFRAMES (global curve), DO NOT FILTER BY NOTE WINDOW
                        var allKeys = new List<(float t, float v)>();
                        foreach (var kf in kfs.OfType<JObject>())
                        {
                            float kt = GetFloat(kf, "m_time", 0f);   // global time
                            float kv = GetFloat(kf, "m_value", 0f);
                            allKeys.Add((Round3(kt), Mathf.Clamp01(kv)));
                            sampleTimes.Add(Round3(kt));
                        }
                        allKeys = allKeys.OrderBy(x => x.t).ToList();

                        // Guards around each note window so edges stay crisp
                        sampleTimes.Add(Mathf.Max(0, Round3(nStart - EPS)));
                        sampleTimes.Add(Round3(nStart));
                        sampleTimes.Add(Round3(nEnd));
                        sampleTimes.Add(Round3(nEnd + EPS));

                        curveLists.Add((nStart, nEnd, scale, allKeys));
                    }
                    else
                    {
                        // Transient rectangle using note length
                        float start = Round3(nStart);
                        float end = Round3(nEnd);
                        transientRects.Add((start, end, Mathf.Clamp01(scale)));

                        sampleTimes.Add(Mathf.Max(0, Round3(start - EPS)));
                        sampleTimes.Add(start);
                        sampleTimes.Add(end);
                        sampleTimes.Add(Round3(end + EPS));
                    }
                }
            }

            if (curveLists.Count == 0 && transientRects.Count == 0)
            {
                Debug.LogWarning(".haps: no notes found (no keyframes or transients).");
            }

            // Evaluate at each sample time (max-mix)
            foreach (var t in sampleTimes)
            {
                // Curves: sample the global keyframe list and window to the note range
                float valFromCurves = 0f;
                foreach (var cl in curveLists)
                {
                    if (t < cl.start - 1e-6f || t > cl.end + 1e-6f) continue;

                    float vCurve;
                    var keys = cl.keys;
                    if (keys.Count == 0) vCurve = 0f;
                    else if (t <= keys[0].t) vCurve = keys[0].v;
                    else if (t >= keys[keys.Count - 1].t) vCurve = keys[keys.Count - 1].v;
                    else
                    {
                        vCurve = 0f;
                        for (int i = 0; i < keys.Count - 1; i++)
                        {
                            var a = keys[i];
                            var b = keys[i + 1];
                            if (t >= a.t && t <= b.t)
                            {
                                float u = Mathf.InverseLerp(a.t, b.t, t);
                                vCurve = Mathf.Lerp(a.v, b.v, u);
                                break;
                            }
                        }
                    }

                    valFromCurves = Mathf.Max(valFromCurves, Mathf.Clamp01(vCurve * cl.scale));
                }

                // Transients (rectangles)
                float valFromRects = 0f;
                foreach (var r in transientRects)
                {
                    if (t + 1e-6f < r.start || t - 1e-6f > r.end) continue;
                    valFromRects = Mathf.Max(valFromRects, r.intensity);
                }

                float vFinal = Mathf.Clamp01(Mathf.Max(valFromCurves, valFromRects));
                hapticTimeStamps.Add(t);
                hapticIntensities.Add(vFinal);
            }
        }
        else
        {
            Debug.LogError($"Unsupported haptic file extension: {ext}");
            return new AnimationCurve();
        }

        // -------- Build final curve (common) --------
        // sort + dedupe (keep last value)
        var ordered = new SortedDictionary<float, float>();
        for (int i = 0; i < hapticTimeStamps.Count; i++)
            ordered[hapticTimeStamps[i]] = hapticIntensities[i];

        var timesList = ordered.Keys.ToList();
        var valsList = ordered.Values.ToList();

        var outCurve = new AnimationCurve();
        for (int i = 0; i < timesList.Count; i++)
        {
            float t = timesList[i];
            float vRaw = valsList[i];
            float v = invert ? Mathf.Clamp01(1f - vRaw * multiplier)
                             : Mathf.Clamp01(vRaw * multiplier);

            int idx = outCurve.AddKey(new Keyframe(t, v));
#if UNITY_EDITOR
            // Tangents: keep .haptic smooth; for .ahap/.haps, square only at tiny step edges (±EPS), linear elsewhere.
            if (!isAhap && !isHaps)
            {
                AnimationUtility.SetKeyLeftTangentMode(outCurve, idx, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(outCurve, idx, AnimationUtility.TangentMode.Linear);
            }
#endif
        }

#if UNITY_EDITOR
        if (isAhap || isHaps)
        {
            for (int i = 0; i < outCurve.length; i++)
            {
                bool hasPrev = i > 0;
                bool hasNext = i < outCurve.length - 1;

                float t  = outCurve.keys[i].time;
                float tl = hasPrev ? outCurve.keys[i - 1].time : t;
                float tr = hasNext ? outCurve.keys[i + 1].time : t;

                float edgeTol = EPS * 1.5f; // tolerate rounding

                bool leftIsStep  = hasPrev && (t - tl) <= edgeTol;
                bool rightIsStep = hasNext && (tr - t) <= edgeTol;

                AnimationUtility.SetKeyLeftTangentMode(
                    outCurve, i,
                    leftIsStep ? AnimationUtility.TangentMode.Constant
                               : AnimationUtility.TangentMode.Linear
                );
                AnimationUtility.SetKeyRightTangentMode(
                    outCurve, i,
                    rightIsStep ? AnimationUtility.TangentMode.Constant
                                : AnimationUtility.TangentMode.Linear
                );
            }
        }
#endif

        return outCurve;
    }

    // ======================= helpers =======================

    private static float Round3(float f) => (float)Math.Round(f, 3);

    private static float GetFloat(JObject obj, string key, float fallback)
    {
        if (obj == null) return fallback;
        var tok = obj[key];
        if (tok == null) return fallback;
        if (tok.Type == JTokenType.Float || tok.Type == JTokenType.Integer) return tok.Value<float>();
        if (tok.Type == JTokenType.String && float.TryParse((string)tok, out var f)) return f;
        return fallback;
    }

    private static float GetEventParam(HapticEvent ev, string id, float defVal)
    {
        if (ev.EventParameters != null)
        {
            var p = ev.EventParameters.FirstOrDefault(x => x.ParameterID == id);
            if (p != null) return p.ParameterValue;
        }
        return defVal;
    }

    // Curve metadata for fast sampling (AHAP)
    [Serializable]
    private class CurveInfo
    {
        public ParameterCurve curve;
        public float absStart, absEnd;            // active range in absolute time
        public List<float> absTimes;              // absolute times of CPs
        public List<ControlPoint> localSorted;    // CPs sorted by local time
    }

    // Linear sample of a single AHAP curve at absolute time t (assumes active)
    private static float SampleCurve(CurveInfo info, float absT)
    {
        var list = info.localSorted;
        float localT = absT - info.curve.Time;

        if (localT <= list[0].Time) return list[0].ParameterValue;
        if (localT >= list[list.Count - 1].Time) return list[list.Count - 1].ParameterValue;

        for (int i = 0; i < list.Count - 1; i++)
        {
            var a = list[i];
            var b = list[i + 1];
            if (localT >= a.Time && localT <= b.Time)
            {
                float u = Mathf.InverseLerp(a.Time, b.Time, localT);
                return Mathf.Lerp(a.ParameterValue, b.ParameterValue, u);
            }
        }
        return 0f;
    }

    // =================== .haptic models ===================
    [Serializable] public class Version { public int major; public int minor; public int patch; }
    [Serializable] public class HapticMetadata { public string editor; public string source; public string project; public string[] tags; public string description; }
    [Serializable] public class AmplitudeEnvelope { public float time; public float amplitude; }
    [Serializable] public class FrequencyEnvelope { public float time; public float frequency; }
    [Serializable] public class Envelopes { public AmplitudeEnvelope[] amplitude; public FrequencyEnvelope[] frequency; }
    [Serializable] public class Continuous { public Envelopes envelopes; }
    [Serializable] public class Signals { public Continuous continuous; }
    [Serializable] public class HapticData { public Version version; public HapticMetadata metadata; public Signals signals; }

    // =================== .ahap models (intensity only) ===================
    [Serializable] public class AHAPData { public List<PatternItem> Pattern; }
    [Serializable] public class PatternItem { public HapticEvent Event; public ParameterCurve ParameterCurve; }
    [Serializable]
    public class HapticEvent
    {
        public string EventType;                 // "HapticContinuous" | "HapticTransient"
        public float Time;                       // seconds
        public float EventDuration;              // continuous only
        public List<EventParameter> EventParameters; // e.g., HapticIntensity, HapticSharpness
    }
    [Serializable] public class EventParameter { public string ParameterID; public float ParameterValue; }
    [Serializable]
    public class ParameterCurve
    {
        public string ParameterID;               // "HapticIntensityControl"
        public List<ControlPoint> ParameterCurveControlPoints;
        public float Time;                       // offset for this curve
    }
    [Serializable] public class ControlPoint { public float Time; public float ParameterValue; }
}
