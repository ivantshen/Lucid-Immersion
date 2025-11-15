using UnityEditor;
using UnityEngine;
using System;

[CanEditMultipleObjects]
[CustomEditor(typeof(AfferenceHaptic), true)]
public class AfferenceHapticCustomEditor : Editor
{
    // Use a property (not const) so it adapts to DPI/theme and avoids CS0133.
    private static float CurveHeight => Mathf.Max(EditorGUIUtility.singleLineHeight + 4f, 20f);

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // --- Common encoder fields ---
        EditorGUILayout.LabelField("Encoder Type", EditorStyles.boldLabel);
        DrawIfExists("lateralEncoderName");
        DrawIfExists("medialEncoderName");
        EditorGUILayout.Space();

        // ---------- Pulse-based event section (incl. new burst options) ----------
        if (target is HapticEventPulse)
        {
            EditorGUILayout.LabelField("Pulse Options", EditorStyles.boldLabel);

            // Legacy/continuous pulse controls
            DrawIfExists("hapticIntensity");
            DrawIfExists("hapticDuration", new GUIContent("Haptic Duration (ms)"));
            DrawIfExists("shouldLoop");
            var aff = (AfferenceHaptic)target;
            if (aff.shouldLoop)
                DrawIfExists("hapticInterval", new GUIContent("Haptic Interval (ms)"));

            // --- Burst Node Hint (moved under shouldLoop) ---
            var hintProp = serializedObject.FindProperty("burstNodeHint");
            if (hintProp != null)
            {
                var hintLabel = new GUIContent(
                    "Burst Node Hint",
                    "Optional key used to select which node to trigger within the BurstModel " +
                    "when multiple burst nodes are defined. Leave blank to use the default node."
                );
                EditorGUILayout.PropertyField(hintProp, hintLabel);
            }

            EditorGUILayout.Space();

            // --- Burst model vs Custom burst switch ---
            EditorGUILayout.LabelField("Burst Configuration", EditorStyles.boldLabel);

            var useCustomBurstProp = serializedObject.FindProperty("useCustomBurst");
            if (useCustomBurstProp != null)
            {
                // Draw the toggle first
                EditorGUILayout.PropertyField(useCustomBurstProp, new GUIContent("Use Custom Burst"));
                // Commit so the next fields reflect the latest toggle
                serializedObject.ApplyModifiedProperties();

                bool usingCustom = useCustomBurstProp.boolValue;

                if (usingCustom)
                {
                    // Show only custom burst payload
                    DrawIfExists("customBurst", includeChildren: true);

                    // Hide initial delay and burst model when custom is active
                    using (new EditorGUI.DisabledScope(true))
                    {
                        DrawIfExists("burstModel");
                        DrawIfExists("initialDelayMs");
                    }
                }
                else
                {
                    // Using BurstModel: show model + its fields, including initialDelayMs
                    DrawIfExists("burstModel");
                    DrawIfExists("initialDelayMs", new GUIContent("Initial Delay (ms)"));
                }
            }
            else
            {
                // Fallback if field not found
                DrawIfExists("customBurst", includeChildren: true);
                DrawIfExists("burstModel");
                DrawIfExists("initialDelayMs");
            }

            EditorGUILayout.Space();
        }

        // ---------- Envelope-based events (curve/clip) ----------
        else if (target is AfferenceHapticEnvelope env)
        {
            bool isCurveOwner = target is HapticEventCurve;

            SerializedProperty useLinearProp = null;
            if (isCurveOwner)
            {
                EditorGUILayout.LabelField("Curve Options", EditorStyles.boldLabel);
                useLinearProp = serializedObject.FindProperty("useLinearTangents");
                if (useLinearProp != null)
                    EditorGUILayout.PropertyField(useLinearProp, new GUIContent("Use Linear Tangents"));
            }

            // Haptic length (drives last key time for HapticEventCurve)
            float hapticLength = 0.5f;
            if (isCurveOwner)
            {
                var lenProp = serializedObject.FindProperty("hapticLength");
                if (lenProp != null)
                {
                    EditorGUILayout.PropertyField(lenProp);
                    serializedObject.ApplyModifiedProperties(); // commit before drawing curve
                    hapticLength = Mathf.Max(0.0001f, lenProp.floatValue);
                }
            }

            if (isCurveOwner)
            {
                // Ensure curve exists
                if (env.hapticIntensity == null)
                    env.hapticIntensity = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(hapticLength, 0f));

                bool useLinear = useLinearProp != null && useLinearProp.boolValue;

#if UNITY_EDITOR
                Undo.RecordObject(env, "Prepare Haptic Curve");
#endif
                // Enforce endpoints/values/tangents BEFORE drawing so the widget shows legal domain
                CurveUtils.EnsureEndpoints(env.hapticIntensity, 0f, hapticLength);
                CurveUtils.EnforceBoundsAndTangents(env.hapticIntensity, useLinear);
                env.hapticIntensity.preWrapMode = WrapMode.ClampForever;
                env.hapticIntensity.postWrapMode = WrapMode.ClampForever;
#if UNITY_EDITOR
                EditorUtility.SetDirty(env);
#endif

                // --- Single compact curve field, framed to [0..len] × [0..1] ---
                var rect = GetCurveRect();
                EditorGUI.BeginChangeCheck();
                var newCurve = EditorGUI.CurveField(
                    rect,
                    new GUIContent("Haptic Intensity"),
                    env.hapticIntensity,
                    Color.green,
                    new Rect(0f, 0f, hapticLength, 1f)
                );

                // Context menu: copy/paste keys + meta
                HandleCurveContextMenu(rect, (HapticEventCurve)target, env, hapticLength, useLinear);

                if (EditorGUI.EndChangeCheck())
                {
#if UNITY_EDITOR
                    Undo.RecordObject(env, "Edit Haptic Curve");
#endif
                    env.hapticIntensity = newCurve;

                    // Re-enforce after edits/paste/drag
                    CurveUtils.EnsureEndpoints(env.hapticIntensity, 0f, hapticLength);
                    CurveUtils.EnforceBoundsAndTangents(env.hapticIntensity, useLinear);
                    env.hapticIntensity.preWrapMode = WrapMode.ClampForever;
                    env.hapticIntensity.postWrapMode = WrapMode.ClampForever;
#if UNITY_EDITOR
                    EditorUtility.SetDirty(env);
#endif
                }
            }
            else if (target is HapticEventClip)
            {
                // Clip decode + preview (normalized to [0..1], clamped, linear tangents)
                var clipProp = serializedObject.FindProperty("hapticClip");
                var gainProp = serializedObject.FindProperty("intensityGain");
                var invertProp = serializedObject.FindProperty("invertClip");

                if (clipProp != null) EditorGUILayout.PropertyField(clipProp);
                if (gainProp != null) EditorGUILayout.PropertyField(gainProp);
                if (invertProp != null) EditorGUILayout.PropertyField(invertProp);

                serializedObject.ApplyModifiedProperties();

#if UNITY_EDITOR
                if (clipProp != null && clipProp.objectReferenceValue != null)
                {
                    string path = AssetDatabase.GetAssetPath(clipProp.objectReferenceValue);
                    bool isSupported =
                        path.EndsWith(".haptic", StringComparison.OrdinalIgnoreCase) ||
                        path.EndsWith(".ahap", StringComparison.OrdinalIgnoreCase) ||
                        path.EndsWith(".haps", StringComparison.OrdinalIgnoreCase);

                    if (!isSupported)
                    {
                        EditorGUILayout.HelpBox("Invalid file type. Please assign a .haptic, .ahap, or .haps file.", MessageType.Error);
                        clipProp.objectReferenceValue = null;
                    }
                    else
                    {
                        float mul = gainProp != null ? gainProp.floatValue : 1f;
                        bool inv = invertProp != null && invertProp.boolValue;

                        // Your existing decoder that returns an AnimationCurve in [0..1] over time
                        env.hapticIntensity = HapticCurveGenerator.GenerateCurve(clipProp.objectReferenceValue, mul, inv);

                        var c = env.hapticIntensity;
                        if (c != null && c.length > 0)
                        {
                            // Linear + clamped for clips (non-HapticEventCurve)
                            CurveUtils.EnforceBoundsAndTangents(c, useLinearTangents: true);
                            c.preWrapMode = WrapMode.ClampForever;
                            c.postWrapMode = WrapMode.ClampForever;

                            float hlen = Mathf.Max(0.0001f, c[c.length - 1].time);

                            var rect = GetCurveRect();
                            EditorGUI.BeginChangeCheck();
                            var newCurve = EditorGUI.CurveField(
                                rect,
                                new GUIContent("Haptic Intensity"),
                                c,
                                Color.green,
                                new Rect(0f, 0f, hlen, 1f)
                            );

                            // Context menu keeps linear for clips
                            HandleCurveContextMenu(rect, curveOwner: null, env, hlen, useLinearTangents: true);

                            if (EditorGUI.EndChangeCheck())
                            {
                                Undo.RecordObject(env, "Edit Haptic Curve");
                                env.hapticIntensity = newCurve;

                                // Re-enforce linear after edits/paste
                                CurveUtils.EnforceBoundsAndTangents(env.hapticIntensity, useLinearTangents: true);
                                env.hapticIntensity.preWrapMode = WrapMode.ClampForever;
                                env.hapticIntensity.postWrapMode = WrapMode.ClampForever;
                                EditorUtility.SetDirty(env);
                            }
                        }
                    }
                }
#else
                if (clipProp != null && clipProp.objectReferenceValue != null)
                    EditorGUILayout.HelpBox("Haptic clip preview requires the UnityEditor API.", MessageType.Info);
#endif
                EditorGUILayout.Space();
            }

            // Looping toggle for envelope types (if they share it)
            DrawIfExists("shouldLoop");
            EditorGUILayout.Space();
        }

        // Final apply for any remaining properties
        if (serializedObject.ApplyModifiedProperties())
        {
#if UNITY_EDITOR
            EditorUtility.SetDirty(target);
#endif
        }
    }

    // ---------------- Helpers ----------------
    private void DrawIfExists(string name, GUIContent label = null, bool includeChildren = false)
    {
        var p = serializedObject.FindProperty(name);
        if (p != null)
        {
            if (label == null) EditorGUILayout.PropertyField(p, includeChildren);
            else EditorGUILayout.PropertyField(p, label, includeChildren);
        }
    }

    // Compact rect for the curve field + label.
    private static Rect GetCurveRect()
    {
        return EditorGUILayout.GetControlRect(false, CurveHeight);
    }

#if UNITY_EDITOR
    private void HandleCurveContextMenu(Rect rect, HapticEventCurve curveOwner, AfferenceHapticEnvelope env, float hlen, bool useLinearTangents)
    {
        var e = Event.current;
        if (e.type == EventType.ContextClick && rect.Contains(e.mousePosition))
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Copy Keys"), false, () =>
            {
                HapticCurveClipboard.CopyFrom(curveOwner, env.hapticIntensity, hlen, useLinearTangents, includeMeta: false);
            });
            menu.AddItem(new GUIContent("Copy Keys + Length + Mode"), false, () =>
            {
                HapticCurveClipboard.CopyFrom(curveOwner, env.hapticIntensity, hlen, useLinearTangents, includeMeta: true);
            });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Paste Keys"), false, () =>
            {
                if (HapticCurveClipboard.TryGet(out var dto))
                {
                    PasteInto(env, dto, keepLength: true, keepMode: true, fallbackHlen: hlen, currentMode: useLinearTangents);
                }
            });
            menu.AddItem(new GUIContent("Paste Keys + Length + Mode"), false, () =>
            {
                if (HapticCurveClipboard.TryGet(out var dto))
                {
                    PasteInto(env, dto, keepLength: false, keepMode: false, fallbackHlen: hlen, currentMode: useLinearTangents);
                }
            });

            menu.ShowAsContext();
            e.Use();
        }
    }

    private void PasteInto(AfferenceHapticEnvelope env, HapticCurveClipboard.CurveDTO dto, bool keepLength, bool keepMode, float fallbackHlen, bool currentMode)
    {
        if (env == null || dto == null || dto.keys == null || dto.keys.Length == 0) return;

        Undo.RecordObject(env, "Paste Haptic Curve");

        // Rebuild keys
        var keys = new Keyframe[dto.keys.Length];
        for (int i = 0; i < dto.keys.Length; i++)
        {
            var d = dto.keys[i];
            var k = new Keyframe(d.time, d.value, d.inTangent, d.outTangent)
            {
                inWeight = d.inWeight,
                outWeight = d.outWeight,
                weightedMode = (WeightedMode)d.weightedMode
            };
            keys[i] = k;
        }

        if (env.hapticIntensity == null)
            env.hapticIntensity = new AnimationCurve(keys);
        else
            env.hapticIntensity.keys = keys;

        // Length / mode decisions
        float hlen = keepLength ? Mathf.Max(0.0001f, env is HapticEventCurve hc ? hc.hapticLength : fallbackHlen)
                                : Mathf.Max(0.0001f, dto.hapticLength > 0 ? dto.hapticLength : fallbackHlen);

        bool useLinear = keepMode ? currentMode : dto.useLinearTangents;

        // If owner has fields, persist them
        if (env is HapticEventCurve owner)
        {
            if (!keepLength) owner.hapticLength = hlen;
            if (!keepMode) owner.useLinearTangents = useLinear;
        }

        // Enforce + clamp
        CurveUtils.EnsureEndpoints(env.hapticIntensity, 0f, hlen);
        CurveUtils.EnforceBoundsAndTangents(env.hapticIntensity, useLinear);
        env.hapticIntensity.preWrapMode = WrapMode.ClampForever;
        env.hapticIntensity.postWrapMode = WrapMode.ClampForever;

        EditorUtility.SetDirty(env);
        Repaint();
    }
#endif
}

// ---------------- Utilities (unchanged behavior) ----------------
static class CurveUtils
{
    public static void EnsureEndpoints(AnimationCurve curve, float firstTime, float lastTime)
    {
        if (curve == null) return;
        if (lastTime <= firstTime) lastTime = firstTime + 0.0001f;

        if (curve.length == 0)
        {
            curve.AddKey(new Keyframe(firstTime, 0f));
            curve.AddKey(new Keyframe(lastTime, 0f));
        }
        else if (curve.length == 1)
        {
            var k = curve[0];
            float v = Mathf.Clamp01(k.value);
            curve.MoveKey(0, new Keyframe(firstTime, v));
            curve.AddKey(new Keyframe(lastTime, v));
        }
        else
        {
            // Clamp any out-of-range times back into [firstTime,lastTime]
            for (int i = 0; i < curve.length; i++)
            {
                var k = curve[i];
                float t = Mathf.Clamp(k.time, firstTime, lastTime);
                if (!Mathf.Approximately(t, k.time))
                {
                    k.time = t;
                    curve.MoveKey(i, k);
                }
            }

            SortByTime(curve);

            // Snap endpoints exactly
            var k0 = curve[0];
            if (!Mathf.Approximately(k0.time, firstTime))
            {
                k0.time = firstTime;
                curve.MoveKey(0, k0);
            }

            int lastIdx = curve.length - 1;
            var kl = curve[lastIdx];
            if (!Mathf.Approximately(kl.time, lastTime))
            {
                kl.time = lastTime;
                curve.MoveKey(lastIdx, kl);
            }
        }

        SortByTime(curve);
    }

    public static void EnforceBoundsAndTangents(AnimationCurve curve, bool useLinearTangents)
    {
        if (curve == null || curve.length == 0) return;

        // Clamp values
        for (int i = 0; i < curve.length; i++)
        {
            var k = curve[i];
            float v = Mathf.Clamp01(k.value);
            if (!Mathf.Approximately(v, k.value))
            {
                k.value = v;
                curve.MoveKey(i, k);
            }
        }

#if UNITY_EDITOR
        var mode = useLinearTangents
            ? AnimationUtility.TangentMode.Linear
            : AnimationUtility.TangentMode.ClampedAuto;

        for (int i = 0; i < curve.length; i++)
        {
            AnimationUtility.SetKeyBroken(curve, i, false);
            AnimationUtility.SetKeyLeftTangentMode(curve, i, mode);
            AnimationUtility.SetKeyRightTangentMode(curve, i, mode);

            // Disable weighted tangents for consistent behavior
            var k = curve[i];
            if (k.weightedMode != WeightedMode.None)
            {
                k.weightedMode = WeightedMode.None;
                curve.MoveKey(i, k);
            }
        }

        // Force internal tangent refresh
        var keys = curve.keys;
        curve.keys = keys;
#endif
    }

    private static void SortByTime(AnimationCurve curve)
    {
        var keys = curve.keys;
        Array.Sort(keys, (a, b) => a.time.CompareTo(b.time));
        curve.keys = keys;
    }
}

#if UNITY_EDITOR
/// Clipboard helper with JSON in system clipboard
static class HapticCurveClipboard
{
    private const string Tag = "HAPTIC_CURVE_V1:";

    [Serializable]
    public class KeyDTO
    {
        public float time, value, inTangent, outTangent, inWeight, outWeight;
        public int weightedMode;
    }

    [Serializable]
    public class CurveDTO
    {
        public float hapticLength;
        public bool useLinearTangents;
        public KeyDTO[] keys;
    }

    public static void CopyFrom(HapticEventCurve owner, AnimationCurve curve, float hlen, bool mode, bool includeMeta)
    {
        if (curve == null) return;

        var dto = new CurveDTO
        {
            hapticLength = includeMeta ? hlen : 0f,
            useLinearTangents = includeMeta ? mode : false,
            keys = new KeyDTO[curve.length]
        };

        for (int i = 0; i < curve.length; i++)
        {
            var k = curve[i];
            dto.keys[i] = new KeyDTO
            {
                time = k.time,
                value = k.value,
                inTangent = k.inTangent,
                outTangent = k.outTangent,
                inWeight = k.inWeight,
                outWeight = k.outWeight,
                weightedMode = (int)k.weightedMode
            };
        }

        EditorGUIUtility.systemCopyBuffer = Tag + JsonUtility.ToJson(dto);
    }

    public static bool TryGet(out CurveDTO dto)
    {
        dto = null;
        string buf = EditorGUIUtility.systemCopyBuffer;
        if (string.IsNullOrEmpty(buf) || !buf.StartsWith(Tag)) return false;
        dto = JsonUtility.FromJson<CurveDTO>(buf.Substring(Tag.Length));
        return dto != null && dto.keys != null && dto.keys.Length > 0;
    }
}
#endif
