#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

[CustomPropertyDrawer(typeof(EncoderNamePopupAttribute))]
public class EncoderNamePopupDrawer : PropertyDrawer
{
    private string[] cachedNames = Array.Empty<string>();
    private double nextScan;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var attr = (EncoderNamePopupAttribute)attribute;

        if (property.propertyType != SerializedPropertyType.String)
        {
            EditorGUI.PropertyField(position, property, label);
            return;
        }

        // Refresh occasionally so new files show up without recompile/entering play
        if (cachedNames.Length == 0 || EditorApplication.timeSinceStartup >= nextScan)
        {
            cachedNames = GetNamesMerged(attr.projectRelativePath, attr.includeSubdirs);
            nextScan = EditorApplication.timeSinceStartup + 2.0;
        }

        var display = attr.includeNone ? (new[] { "None" }).Concat(cachedNames).ToArray() : cachedNames;

        if (display.Length == 0)
        {
            using (new EditorGUI.DisabledScope(true))
                EditorGUI.Popup(position, label.text, 0, new[] { "(no encoders found)" });
            property.stringValue = "";
            return;
        }

        // Map current string to popup index
        int current = 0; // "None"
        if (!string.IsNullOrEmpty(property.stringValue))
        {
            int nameIdx = Array.IndexOf(cachedNames, property.stringValue);
            current = attr.includeNone ? (nameIdx < 0 ? 0 : nameIdx + 1) : Math.Max(0, nameIdx);
        }

        int chosen = EditorGUI.Popup(position, label.text, current, display);
        if (chosen != current)
        {
            if (attr.includeNone && chosen == 0) property.stringValue = "";
            else property.stringValue = attr.includeNone ? cachedNames[chosen - 1] : cachedNames[chosen];
        }
    }

    /// <summary>
    /// Returns a union of encoder names (filenames w/o .json) with priority:
    ///   1) persistent root + /Encoders (top only)
    ///   2) StreamingAssets/Encoders (adds new names only)
    ///   3) projectRelativePath (optional; adds new names only)
    /// No copying; just reads.
    /// </summary>
    private static string[] GetNamesMerged(string projectRelativePath, bool includeSubdirs)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        string pRoot = Application.persistentDataPath;
        string pEnc = Path.Combine(pRoot, "Encoders");
        string sEnc = Path.Combine(Application.streamingAssetsPath, "Encoders");
        string proj = ResolveProjectRelativeToAbsolute(projectRelativePath);

        // Ensure persistent/Encoders exists for convenience in dev
        try { if (!Directory.Exists(pEnc)) Directory.CreateDirectory(pEnc); } catch { }

        // helpers
        static IEnumerable<string> ReadNames(string dir, bool subdirs)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) yield break;
            var opt = subdirs ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            foreach (var f in Directory.GetFiles(dir, "*.json", opt))
                yield return Path.GetFileNameWithoutExtension(f);
        }

        // 1) persistent first (root + Encoders)
        foreach (var n in ReadNames(pRoot, subdirs: false)) names.Add(n);
        foreach (var n in ReadNames(pEnc, subdirs: false)) names.Add(n);

        // 2) streaming adds any missing
        foreach (var n in ReadNames(sEnc, includeSubdirs)) names.Add(n);

        // 3) optional project path adds any missing
        foreach (var n in ReadNames(proj, includeSubdirs)) names.Add(n);

        var result = names.OrderBy(n => n, StringComparer.Ordinal).ToArray();

        // Uncomment if you want a trace:
        // Debug.Log($"[Encoder Popup] merged\n  persistent root     : {pRoot}\n  persistent/Encoders : {pEnc}\n  streaming/Encoders  : {sEnc}\n  project path        : {proj}\n  found               : [{string.Join(", ", result)}]");

        if (result.Length == 0)
        {
            Debug.Log($"[Encoder Popup] No encoders found.\n" +
                      $"  persistent root     : {pRoot}\n" +
                      $"  persistent/Encoders : {pEnc}\n" +
                      $"  streaming/Encoders  : {sEnc}\n" +
                      $"  project path        : {proj}");
        }

        return result;
    }

    /// <summary>
    /// Accepts "Assets/...", "Whatever/...", or absolute; returns absolute path or null.
    /// </summary>
    private static string ResolveProjectRelativeToAbsolute(string projectRelativePath)
    {
        if (string.IsNullOrEmpty(projectRelativePath)) return null;

        string p = projectRelativePath.Replace("\\", "/");
        if (Path.IsPathRooted(p)) return Directory.Exists(p) ? p : null;

        if (p.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            p = p.Substring(7);

        string abs = Path.Combine(Application.dataPath, p);
        return Directory.Exists(abs) ? abs : null;
    }
}
#endif
