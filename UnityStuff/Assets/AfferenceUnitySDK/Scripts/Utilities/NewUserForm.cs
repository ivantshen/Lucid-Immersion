using AfferenceEngine.src.Core.Entities;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

public class NewUserForm : MonoBehaviour
{
    [System.Serializable]
    public class TextInputRef
    {
        public TMP_InputField tmp;
        public InputField uui; // legacy Unity UI InputField

        public string Text
        {
            get => tmp ? tmp.text : (uui ? uui.text : string.Empty);
            set
            {
                if (tmp) tmp.text = value;
                else if (uui) uui.text = value;
            }
        }

        public void AddOnChangedListener(UnityAction<string> listener)
        {
            if (tmp) tmp.onValueChanged.AddListener(listener);
            else if (uui) uui.onValueChanged.AddListener(listener);
        }

        public bool IsAssigned => tmp || uui;
    }

    public TMP_Text statusText;

    [Header("Name Inputs (assign TMP or Unity UI)")]
    public TextInputRef firstInput;
    public TextInputRef middleInput;
    public TextInputRef lastInput;

    [Header("Actions/UI")]
    public Button createButton;
    public GameObject nameConflictWarning;

    [Header("Callbacks (invoke via Inspector)")]
    [Tooltip("Fires ONLY after a new user file is successfully created and loaded.")]
    public UnityEvent onUserCreated;
    [Tooltip("Optional: fires when a name conflict is detected.")]
    public UnityEvent onNameConflict;

    string usersDir => Path.Combine(Application.persistentDataPath, "Users");

    private void Awake()
    {
        Directory.CreateDirectory(usersDir);

        if (createButton)
        {
            createButton.onClick.RemoveListener(HandleCreateClicked);
            createButton.onClick.AddListener(HandleCreateClicked);
        }

        // Hook validation to whichever inputs are assigned
        if (firstInput?.IsAssigned == true) firstInput.AddOnChangedListener(_ => Validate());
        if (middleInput?.IsAssigned == true) middleInput.AddOnChangedListener(_ => Validate());
        if (lastInput?.IsAssigned == true) lastInput.AddOnChangedListener(_ => Validate());

        Validate();

        if (nameConflictWarning) nameConflictWarning.SetActive(false);

        statusText.text = "";
    }

    private void Update()
    {
        if (statusText && HapticManager.Instance != null)
            statusText.text = HapticManager.Instance.status;
    }

    private void Validate()
    {
        // Require First + Last, Middle optional
        bool valid =
            firstInput != null && !string.IsNullOrWhiteSpace(firstInput.Text) &&
            lastInput != null && !string.IsNullOrWhiteSpace(lastInput.Text);

        if (createButton) createButton.interactable = valid;
    }

    private async void HandleCreateClicked()
    {
        if (TryCreateUser(out string fileBase))
        {
            HapticManager.Instance.LoadUser(fileBase);      // 1)load

            createButton.interactable = false;
            HapticManager.Instance.status = "Checking permissions";
            await System.Threading.Tasks.Task.Delay(100);

#if UNITY_ANDROID && !UNITY_EDITOR
    bool permOK = await HapticManager.Instance.EnsureBlePermissionsAsync();
    if (!permOK) { Debug.LogWarning("BLE permissions denied; aborting connect."); return; }

    await HapticManager.Instance.WaitForAndroidFocusAndStabilityAsync(350);
#endif

            bool ok = await HapticManager.Instance.ConnectCurrentUserAsync(); // 2) connect

            createButton.interactable = true;
            if (ok) onUserCreated?.Invoke(); // only on success
                                             // else: status labels already show "Please restart the app."
        }
        else
        {
            onNameConflict?.Invoke();
        }
    }

    private bool TryCreateUser(out string fileBase)
    {
        if (nameConflictWarning) nameConflictWarning.SetActive(false);

        var first = (firstInput?.Text ?? "").Trim();
        var middle = (middleInput?.Text ?? "").Trim();
        var last = (lastInput?.Text ?? "").Trim();

        fileBase = $"{first}{middle}{last}";

        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(last))
        {
            Debug.LogWarning("First and Last name are required.");
            return false;
        }

        // Compare using normalized version only (case/符号-insensitive)
        if (NameExists(fileBase))
        {
            Debug.LogWarning($"User name conflict: a user file for '{first} {middle} {last}' already exists.");
            if (nameConflictWarning) nameConflictWarning.SetActive(true);
            return false;
        }

        // Build the intended filename with *original casing*
        var targetPath = Path.Combine(usersDir, fileBase + ".json");

        // Save with original casing
        User CreatedUser = new User { Name = new User.NameFormat(first, middle, last) };
        CreatedUser.SaveUserData(targetPath);

        //// Load by base filename (no extension)
        //HapticManager.Instance.LoadUser(fileBase);

        return true;
    }

    // ---- Helpers ----
    // Normalize to alphanumeric only, lowercased (for case-insensitive comparisons & filename safety)
    static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;

        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    private bool NameExists(string candidateBase)
    {
        var normalizedCandidate = Normalize(candidateBase);
        foreach (var file in Directory.GetFiles(usersDir, "*.json", SearchOption.TopDirectoryOnly))
        {
            var existingName = Path.GetFileNameWithoutExtension(file);
            if (Normalize(existingName) == normalizedCandidate)
                return true;
        }
        return false;
    }
}
