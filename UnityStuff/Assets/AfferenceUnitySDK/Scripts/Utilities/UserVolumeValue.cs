using UnityEngine;
using TMPro;
using UnityEngine.UI;


public class UserVolumeValue : MonoBehaviour
{
    public Text ui_userVolume;
    public TMP_Text tmp_userVolume;

    // Update is called once per frame
    void Update()
    {
        if(HapticManager.Instance != null)
        {
            if (ui_userVolume != null) { ui_userVolume.text = HapticManager.Instance.userVolume.ToString("F2"); }
            if (tmp_userVolume != null) { tmp_userVolume.text = HapticManager.Instance.userVolume.ToString("F2"); }
        }
    }
}
