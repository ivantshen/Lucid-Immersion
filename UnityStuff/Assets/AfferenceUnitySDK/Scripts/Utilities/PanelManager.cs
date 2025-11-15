using UnityEngine;

public class PanelManager : MonoBehaviour
{
    public GameObject[] panels;

    public GameObject[] platformDepItems;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
#if UNITY_STANDALONE_WIN || (UNITY_EDITOR_WIN && !UNITY_ANDROID && !UNITY_IOS)
        SetActivePanel(0);
#else
        SetActivePanel(2);
        foreach(GameObject item in platformDepItems){ item.SetActive(false); }
#endif
    }

    public void SetActivePanel(int panel)
    {
        for (int i = 0; i < panels.Length; i++)
            panels[i].SetActive(panel >= 0 && i == panel);
    }

    public void BackButton(int panel)
    {
        if(panel == 0 && (Application.platform == RuntimePlatform.IPhonePlayer || Application.platform == RuntimePlatform.Android))
        {
            SetActivePanel(2);
        }
        else { SetActivePanel(0); }
    }
}
