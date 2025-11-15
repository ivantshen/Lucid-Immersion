using UnityEngine;

public class PulseSender : MonoBehaviour
{
    public HapticEventPulse hapticEventPulse;
    public float totalHapticTime = 2;
    private float timer;
    public float pulseHz = 10;
    private float pulseTime, lastTime;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        pulseTime = 1 / pulseHz;
    }

    // Update is called once per frame
    void Update()
    {
        if (timer > 0)
        {
            timer -= Time.deltaTime;
            if(timer <= lastTime - pulseTime)
            {
                lastTime = timer;
                hapticEventPulse.customBurst[0].y = Mathf.Clamp01(timer/totalHapticTime);
                hapticEventPulse.PlayHaptic();
            }
        }
    }

    public void PlayHaptic()
    {
        timer = totalHapticTime;
        lastTime = timer;
    }
}
