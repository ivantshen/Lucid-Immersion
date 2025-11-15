using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UIButtonBlocker : MonoBehaviour
{
    public GameObject[] buttonBlockers;
    private float timer;
    private bool blockerState, isTimed;
    private float totalTime;

    // Start is called before the first frame update
    void Start()
    {
        foreach (GameObject blocker in buttonBlockers)
        {
            blocker.GetComponent<Image>().alphaHitTestMinimumThreshold = 0f; // 0 = block all pixels
        }
        
        BlockButtons(false);
    }

    // Update is called once per frame
    void Update()
    {
        if (isTimed)
        {
            if (timer > 0)
            {
                timer -= Time.deltaTime;
                foreach (GameObject blocker in buttonBlockers)
                {
                    blocker.GetComponent<Image>().fillAmount = Mathf.Clamp01(timer / totalTime);
                }
            }
            else { if (blockerState) { BlockButtons(false); isTimed = false; } }
        }
    }

    public void BlockButtonsForTime(float time)
    {
        BlockButtons(true);
        timer = time;
        totalTime = time;
        isTimed = true;
    }

    public void BlockButtons(bool active)
    {
        foreach(GameObject blocker in buttonBlockers)
        {
            blocker.SetActive(active);
        }
        blockerState = active;
    }
}
