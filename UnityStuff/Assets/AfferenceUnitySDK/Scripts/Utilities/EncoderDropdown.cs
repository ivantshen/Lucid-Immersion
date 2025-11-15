using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(TMP_Dropdown))]
public class EncoderDropdown : MonoBehaviour, IPointerClickHandler
{
    [Header("UI")]
    [SerializeField] private TMP_Dropdown dropdown;

    [Header("Options")]
    [Tooltip("Adds a '(None)' option that clears encoder names.")]
    [SerializeField] private bool includeNoneOption = true;
    [SerializeField] private string noneLabel = "(None)";

    [Tooltip("Include inactive AfferenceHaptics in scene.")]
    [SerializeField] private bool includeInactive = false;

    [Tooltip("After applying, also send a single 0f to guarantee silence.")]
    [SerializeField] private bool sendZeroAfterApply = true;

    [Header("List sizing")]
    [Tooltip("Exact height (px) for each dropdown option.")]
    [SerializeField] private float optionHeight = 40f;

    [Tooltip("Extra space (px) between options.")]
    [SerializeField] private float optionSpacing = 0f;

    private string[] encoderNames = System.Array.Empty<string>();

    void Reset() => dropdown = GetComponent<TMP_Dropdown>();

    void OnEnable()
    {
        if (!dropdown) dropdown = GetComponent<TMP_Dropdown>();
        PopulateOptions();
        dropdown.onValueChanged.AddListener(OnDropdownChanged);

        // Apply current selection immediately on start
        ApplyToAll(IndexToName(dropdown.value));
    }

    void OnDisable()
    {
        if (dropdown) dropdown.onValueChanged.RemoveListener(OnDropdownChanged);
    }

    [ContextMenu("Refresh Encoder Options")]
    public void PopulateOptions()
    {
        encoderNames = AfferenceHaptic.ListEncoderNames() ?? System.Array.Empty<string>();

        var options = new List<string>(encoderNames.Length + (includeNoneOption ? 1 : 0));
        if (includeNoneOption) options.Add(noneLabel);
        options.AddRange(encoderNames);

        dropdown.ClearOptions();
        dropdown.AddOptions(options);
        dropdown.SetValueWithoutNotify(0);
        dropdown.RefreshShownValue();
    }

    private void OnDropdownChanged(int index)
    {
        string chosen = IndexToName(index);
        ApplyToAll(chosen);
    }

    private string IndexToName(int index)
    {
        if (includeNoneOption)
        {
            if (index == 0) return null;
            int idx = index - 1;
            return (idx >= 0 && idx < encoderNames.Length) ? encoderNames[idx] : null;
        }
        return (index >= 0 && index < encoderNames.Length) ? encoderNames[index] : null;
    }

    [ContextMenu("Apply Current Selection")]
    public void ApplyToAll(string encoderName = null)
    {
        if (dropdown && encoderName == null)
            encoderName = IndexToName(dropdown.value);

        var allHaptics = FindAllAfferenceHaptics();

        foreach (var h in allHaptics)
        {
            string val = encoderName ?? string.Empty;
            h.lateralEncoderName = val;
            h.medialEncoderName = val;
        }

        if (sendZeroAfterApply && HapticManager.Instance != null)
            HapticManager.Instance.SendHaptic(0f);

        Debug.Log($"[EncoderDropdown] Applied '{(encoderName ?? "(None)")}' to {allHaptics.Length} AfferenceHaptics.");
    }

    private AfferenceHaptic[] FindAllAfferenceHaptics()
    {
#if UNITY_2023_1_OR_NEWER
        if (includeInactive)
            return Resources.FindObjectsOfTypeAll<AfferenceHaptic>().Where(IsInScene).ToArray();
        return FindObjectsByType<AfferenceHaptic>(FindObjectsSortMode.None);
#else
        if (includeInactive)
            return Resources.FindObjectsOfTypeAll<AfferenceHaptic>().Where(IsInScene).ToArray();
        return GameObject.FindObjectsOfType<AfferenceHaptic>();
#endif
    }

    private static bool IsInScene(Object o)
    {
        var go = o as GameObject ?? (o as Component)?.gameObject;
        if (!go) return false;
#if UNITY_EDITOR
        if (UnityEditor.EditorUtility.IsPersistent(go)) return false;
#endif
        return go.scene.IsValid();
    }

    // ----------------- List sizing on open -----------------
    public void OnPointerClick(PointerEventData eventData)
    {
        StartCoroutine(ResizeSpawnedListNextFrame());
    }

    private IEnumerator ResizeSpawnedListNextFrame()
    {
        // Wait for TMP to instantiate the dropdown list
        yield return null;
        yield return null;

        var sr = FindActiveDropdownScrollRect();
        if (!sr) yield break;

        var listRoot = sr.GetComponent<RectTransform>();
        var viewport = sr.viewport;
        var content = sr.content;
        if (!listRoot || !viewport || !content) yield break;

        int optionsCount = dropdown.options.Count;
        if (optionsCount <= 0) yield break;

        // Total = optionHeight * count + spacing * (count - 1)
        float total = (optionsCount * optionHeight) + Mathf.Max(0, optionsCount - 1) * optionSpacing;

        // Force each row height
        for (int i = 0; i < content.childCount; i++)
        {
            var child = content.GetChild(i) as RectTransform;
            if (!child) continue;

            child.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, optionHeight);
            var le = child.GetComponent<LayoutElement>();
            if (le)
            {
                le.minHeight = optionHeight;
                le.preferredHeight = optionHeight;
                le.flexibleHeight = 0;
            }
        }

        // Disable scrolling; hide scrollbar
        sr.vertical = false;
        sr.movementType = ScrollRect.MovementType.Unrestricted;
        if (sr.verticalScrollbar) sr.verticalScrollbar.gameObject.SetActive(false);

        // Set content, viewport, and background to total height
        SetHeight(content, total);
        SetHeight(viewport, total);
        SetHeight(listRoot, total);

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(listRoot);

        // Optional: scroll to top
        sr.verticalNormalizedPosition = 1f;
    }

    private static void SetHeight(RectTransform rt, float h)
    {
        if (!rt) return;
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);
    }

    private static ScrollRect FindActiveDropdownScrollRect()
    {
        var all = GameObject.FindObjectsOfType<ScrollRect>(true);
        return all.FirstOrDefault(sr =>
            sr && sr.gameObject.activeInHierarchy &&
            (sr.gameObject.name.Contains("TMP Dropdown") ||
             sr.gameObject.name.Contains("Dropdown List")));
    }
}
