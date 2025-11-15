using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

public class ExistingUserDropdown : MonoBehaviour
{
    [Header("Assign Dropdown type (either one)")]
    public TMP_Text statusText;
    public TMP_Dropdown tmpDropdown;
    public Dropdown uiDropdown;
    public Button continueButton;

    private readonly List<string> names = new List<string>();
    private string usersDir => Path.Combine(Application.persistentDataPath, "Users");

    private string selectedUser;

    public UnityEvent onContinueAfterConnect;


    private void OnEnable()
    {
        BuildNames();
        SetUsers();
        HookListeners();
        statusText.text = "";
    }

    private void OnDisable()
    {
        UnhookListeners();
    }

    private void Update()
    {
        if (statusText && HapticManager.Instance != null)
            statusText.text = HapticManager.Instance.status;
    }

    private void BuildNames()
    {
        names.Clear();

        if (!Directory.Exists(usersDir)) return;

        names.AddRange(
            Directory.EnumerateFiles(usersDir, "*.json", SearchOption.TopDirectoryOnly)
                     .Select(Path.GetFileNameWithoutExtension)
                     .OrderBy(n => n)
        );
    }

    private void SetUsers()
    {
        // Clear previous options
        if (tmpDropdown)
        {
            tmpDropdown.ClearOptions();
            tmpDropdown.interactable = names.Count > 0;
        }
        else if (uiDropdown)
        {
            uiDropdown.ClearOptions();
            uiDropdown.interactable = names.Count > 0;
        }

        if (continueButton) continueButton.interactable = names.Count > 0;

        if (names.Count == 0)
        {
            var none = new List<string> { "(no users found)" };
            if (tmpDropdown)
            {
                tmpDropdown.AddOptions(none);
                tmpDropdown.value = 0;
                tmpDropdown.RefreshShownValue();
            }
            else if (uiDropdown)
            {
                foreach (var s in none) uiDropdown.options.Add(new Dropdown.OptionData(s));
                uiDropdown.value = 0;
                uiDropdown.RefreshShownValue();
            }
            return;
        }

        // Add options
        if (tmpDropdown)
        {
            tmpDropdown.AddOptions(names);
            int idx = Mathf.Clamp(tmpDropdown.value, 0, names.Count - 1);
            tmpDropdown.SetValueWithoutNotify(idx);
            tmpDropdown.RefreshShownValue();
            LoadUserByIndex(idx);
        }
        else if (uiDropdown)
        {
            foreach (var s in names) uiDropdown.options.Add(new Dropdown.OptionData(s));
            int idx = Mathf.Clamp(uiDropdown.value, 0, names.Count - 1);
            uiDropdown.SetValueWithoutNotify(idx);
            uiDropdown.RefreshShownValue();
            LoadUserByIndex(idx);
        }
    }

    private void HookListeners()
    {
        continueButton.onClick.AddListener(LoadUser);

        if (tmpDropdown)
        {
            tmpDropdown.onValueChanged.AddListener(OnTmpChanged);
        }
        else if (uiDropdown)
        {
            uiDropdown.onValueChanged.AddListener(OnUiChanged);
        }
    }

    private void UnhookListeners()
    {
        if (tmpDropdown)
        {
            tmpDropdown.onValueChanged.RemoveListener(OnTmpChanged);
        }
        if (uiDropdown)
        {
            uiDropdown.onValueChanged.RemoveListener(OnUiChanged);
        }
    }

    private async void LoadUser()
    {
        if (string.IsNullOrEmpty(selectedUser))
        {
            Debug.Log("No user selected");
            return;
        }

        HapticManager.Instance.LoadUser(selectedUser);

        continueButton.interactable = false;
        HapticManager.Instance.status = "Checking permissions";
        await System.Threading.Tasks.Task.Delay(100);

#if UNITY_ANDROID && !UNITY_EDITOR
    bool permOK = await HapticManager.Instance.EnsureBlePermissionsAsync();
    if (!permOK) { Debug.LogWarning("BLE permissions denied; aborting connect."); return; }

    await HapticManager.Instance.WaitForAndroidFocusAndStabilityAsync(350);
#endif

        bool ok = await HapticManager.Instance.ConnectCurrentUserAsync();

        if (ok)
        {
            continueButton.interactable = true;
            onContinueAfterConnect?.Invoke(); // only runs if connection succeeds
        }
        // if not ok, HapticManager already updates the status labels
    }


    private void OnTmpChanged(int index)
    {
        if (index < 0 || index >= names.Count) return;
        selectedUser = names[index];
    }

    private void OnUiChanged(int index)
    {
        if (index < 0 || index >= names.Count) return;
        selectedUser = names[index];
    }

    private void LoadUserByIndex(int index)
    {
        selectedUser = names[index];
        if (index < 0 || index >= names.Count) return;
    }
}
