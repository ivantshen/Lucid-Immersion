using System;
#if UNITY_STANDALONE_WIN || (UNITY_EDITOR_WIN && !UNITY_ANDROID && !UNITY_IOS)
using System.IO.Ports;
#endif
using System.Linq;   
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ComPortDropdown : MonoBehaviour
{
    [Header("Assign Dropdown type")]
    public TMP_Dropdown tmpDropdown;
    public Dropdown uiDropdown;

    void Start()
    {
        SetCOMPorts();
        HookListeners();
    }

    public void SetCOMPorts()
    {
#if UNITY_STANDALONE_WIN || (UNITY_EDITOR_WIN && !UNITY_ANDROID && !UNITY_IOS)
        // Get ports, trim, remove empties, and de-dupe (case-insensitive)
        var ports = SerialPort.GetPortNames()
            .Select(p => (p ?? "").Trim())
            .Where(p => p.Length > 0)
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (tmpDropdown)
        {
            tmpDropdown.ClearOptions();
            tmpDropdown.AddOptions(ports);
            if (ports.Count > 0)
            {
                tmpDropdown.value = 0;
                HapticManager.Instance.SetPort(tmpDropdown.options[0].text);
            }
            tmpDropdown.RefreshShownValue();
        }
        else if (uiDropdown)
        {
            uiDropdown.ClearOptions();
            foreach (var port in ports)
                uiDropdown.options.Add(new Dropdown.OptionData(port));
            if (uiDropdown.options.Count > 0)
            {
                uiDropdown.value = 0;
                HapticManager.Instance.SetPort(tmpDropdown.options[0].text);
            }
            uiDropdown.RefreshShownValue();
        }
#endif
    }

    private void HookListeners()
    {
        if (tmpDropdown)
        {
            tmpDropdown.onValueChanged.AddListener(index =>
            {
                string selected = tmpDropdown.options[index].text;
                HapticManager.Instance.SetPort(selected);
            });
        }
        else if (uiDropdown)
        {
            uiDropdown.onValueChanged.AddListener(index =>
            {
                string selected = uiDropdown.options[index].text;
                HapticManager.Instance.SetPort(selected);
            });
        }
    }
}
