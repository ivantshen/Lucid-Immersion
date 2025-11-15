using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public class CopyAssets
{
    // Toggle recursion if you ever add nested subfolders under your StreamingAssets subfolders.
    private const bool RECURSIVE = true;

    // If you want to overwrite existing files, set this to true.
    private const bool OVERWRITE = false;

    /// <summary>
    /// Drop-in entry point (same signature as your original).
    /// Copies:
    ///   - StreamingAssets/BodyLocations/*.json
    ///   - StreamingAssets/Users/*.json
    ///   - StreamingAssets/Encoders/*.json and *.pos
    /// into Application.persistentDataPath under the same-named subfolders.
    /// </summary>
    public void CopyAllAssets()
    {
        // Body: copy *.json
        CopyFolder(
            streamingSubfolder: "BodyLocations",
            persistentSubfolder: "BodyLocations",
            allowedExtensions: new HashSet<string> { ".json" });

        // Users: copy *.json
        CopyFolder(
            streamingSubfolder: "Users",
            persistentSubfolder: "Users",
            allowedExtensions: new HashSet<string> { ".json" });

        // Encoders: copy *.json and *.pos
        CopyFolder(
            streamingSubfolder: "Encoders",
            persistentSubfolder: "Encoders",
            allowedExtensions: new HashSet<string> { ".json", ".pos" });
    }

    // ---------------- Core logic ----------------

    private void CopyFolder(string streamingSubfolder, string persistentSubfolder, HashSet<string> allowedExtensions)
    {
        string dstDir = Path.Combine(Application.persistentDataPath, persistentSubfolder);
        Directory.CreateDirectory(dstDir);

        // 1) Enumerate relative file paths under StreamingAssets/<subfolder>
        List<string> relFiles = EnumerateStreamingFiles(streamingSubfolder);

        // 2) Filter by extension and copy
        foreach (var rel in relFiles)
        {
            string filename = Path.GetFileName(rel);
            if (string.IsNullOrEmpty(filename)) continue;

            string ext = Path.GetExtension(filename).ToLowerInvariant();
            if (!allowedExtensions.Contains(ext)) continue;

            string dstPath = Path.Combine(dstDir, filename);
            if (!OVERWRITE && File.Exists(dstPath)) continue;

            string url = BuildStreamingUrl(rel);
            try
            {
                byte[] data = DownloadBytesSync(url);
                if (data != null && data.Length > 0)
                {
                    // Ensure dir exists (in case you enable recursion and want to preserve structure)
                    Directory.CreateDirectory(Path.GetDirectoryName(dstPath));
                    File.WriteAllBytes(dstPath, data);
                    // Debug.Log($"Copied {rel} -> {dstPath}");
                }
                else
                {
                    Debug.LogWarning($"No data read for {url}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Copy failed for {url} -> {dstPath}\n{ex}");
            }
        }
    }

    /// <summary>
    /// Returns a list of relative file paths under StreamingAssets/<streamingSubfolder>.
    /// On Android, uses AssetManager.list() (optionally recursive). Elsewhere, uses Directory.GetFiles.
    /// Paths are returned with forward slashes.
    /// </summary>
    private List<string> EnumerateStreamingFiles(string streamingSubfolder)
    {
        var results = new List<string>();
        string cleanSub = streamingSubfolder.Trim('/', '\\');

#if UNITY_ANDROID && !UNITY_EDITOR
        EnumerateAndroidStreaming(results, cleanSub);
#else
        string srcDir = Path.Combine(Application.streamingAssetsPath, cleanSub);
        if (!Directory.Exists(srcDir)) return results;

        var searchOption = RECURSIVE ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        foreach (var absPath in Directory.GetFiles(srcDir, "*.*", searchOption))
        {
            // Build relative path under StreamingAssets/<subfolder>
            string relFromSub = absPath.Substring(srcDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string rel = string.IsNullOrEmpty(cleanSub) ? relFromSub : $"{cleanSub}/{relFromSub}";
            results.Add(rel.Replace("\\", "/"));
        }
#endif
        return results;
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    /// <summary>
    /// Android-only enumeration via AssetManager.list(). Recurses if RECURSIVE = true.
    /// </summary>
    private void EnumerateAndroidStreaming(List<string> outList, string folder)
    {
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var assetManager = activity.Call<AndroidJavaObject>("getAssets"))
            {
                void Recurse(string rel)
                {
                    // list() returns child names (files and directories) under the given path
                    string[] names = assetManager.Call<string[]>("list", rel);
                    if (names == null || names.Length == 0)
                    {
                        // If nothing returned, rel might actually be a file; leave filtering to caller
                        if (!string.IsNullOrEmpty(rel))
                            outList.Add(rel.Replace("\\", "/"));
                        return;
                    }

                    foreach (var name in names)
                    {
                        if (string.IsNullOrEmpty(name)) continue;
                        var child = string.IsNullOrEmpty(rel) ? name : $"{rel}/{name}";

                        string[] probe = assetManager.Call<string[]>("list", child);
                        bool isDir = probe != null && probe.Length > 0;

                        if (isDir)
                        {
                            if (RECURSIVE) Recurse(child);
                            // If not recursive, skip nested directories
                        }
                        else
                        {
                            outList.Add(child.Replace("\\", "/"));
                        }
                    }
                }

                Recurse(folder);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"AssetManager enumeration failed for '{folder}': {e}");
        }
    }
#endif

    // ---------------- IO helpers ----------------

    /// <summary>
    /// Builds a URL that UnityWebRequest can read from StreamingAssets on each platform.
    /// </summary>
    private static string BuildStreamingUrl(string relativePath)
    {
        string basePath = Application.streamingAssetsPath.Replace("\\", "/");
        string rel = relativePath.TrimStart('/', '\\').Replace("\\", "/");
        string full = $"{basePath}/{rel}";

        // On Android, streamingAssetsPath already includes jar:file://... for APK.
        if (!full.Contains("://"))
            full = "file://" + full;

        return full;
    }

    /// <summary>
    /// Synchronous download using UnityWebRequest to read from StreamingAssets URL.
    /// Keeps your public API non-Coroutine. Call during boot/splash to avoid frame hiccups.
    /// </summary>
    private static byte[] DownloadBytesSync(string url)
    {
        using (var req = UnityWebRequest.Get(url))
        {
            var op = req.SendWebRequest();
            // Busy-wait; add a tiny sleep to avoid pegging CPU during copy of many files
            while (!op.isDone) { System.Threading.Thread.Sleep(1); }

#if UNITY_2020_2_OR_NEWER
            if (req.result != UnityWebRequest.Result.Success)
#else
            if (req.isNetworkError || req.isHttpError)
#endif
            {
                throw new IOException($"UnityWebRequest error: {req.error} [{url}]");
            }

            return req.downloadHandler.data;
        }
    }
}
