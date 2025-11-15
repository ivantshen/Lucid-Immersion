using UnityEngine;

/// Use on a string field to render a dropdown of JSONs from a folder.
/// Stores the chosen file *name* (no extension). "None" => empty string.
public class EncoderNamePopupAttribute : PropertyAttribute
{
    // Default to your folder: Assets/AfferenceUnitySDK/StreamingAssets/Encoders
    public readonly string projectRelativePath;
    public readonly bool includeSubdirs;
    public readonly bool includeNone;

    public EncoderNamePopupAttribute(
        string projectRelativePath = "Assets/StreamingAssets/Encoders",
        bool includeSubdirs = false,
        bool includeNone = true)
    {
        this.projectRelativePath = projectRelativePath;
        this.includeSubdirs = includeSubdirs;
        this.includeNone = includeNone;
    }
}