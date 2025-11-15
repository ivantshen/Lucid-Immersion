using AfferenceEngine.src.Core.Entities;
using AfferenceEngine.src.Core.Other;
using AfferenceEngine.src.Core.StimulationLogic;
using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class Calibration : MonoBehaviour
{
    //0-Square
    //1-Triangle
    //2-Sine
    //3-Gaussian
    //4-ExpInc
    //5-LinInc
    //6-Efficient
    //7-ExpDec

    private string usersDir => Path.Combine(Application.persistentDataPath, "Users");

    public bool calibrationComplete;
    public int calibrationStep = 0;

    public int calibrationMode = 0; // 0 = PA 1 = PW
    public int minMax = 0;

    public TMP_Text calibrationTitle;
    public int lateralMedial = 0; // 0 = Lateral 1 = Medial

    private int waveform = 2;

    public double continuousCalFrequency = 20;
    public double tapCalFrequency = 3;
    private double calFrequency = 20;

    //PA calibration
    private double PA_CalibrationPW = 250;
    private float continuousPA_CalibrationDuration = .25f;
    private float tapPA_CalibrationDuration = 1f;
    private float PA_CalibrationPulseDuration = .25f;
    private double PA_Increment = .1f;

    //PW calibration
    private double PW_CalibrationPA = 9;
    private float continuousPW_CalibrationDuration = .25f;
    private float tapPW_CalibrationDuration = 1f;
    private float PW_CalibrationPulseDuration = .25f;
    private double PW_Increment = 2;

    private float testPulseTimer;
    private bool sendingTestPulse;

    public double userPA_Threshold, userPA_Maximum, userPW_Threshold, userPW_Maximum;

    public Image[] stepHighlights;
    public GameObject[] checkMarks;

    public UnityEvent calibrationCompleteEvent;

    private StimPoint[] stimpoints = new StimPoint[4];

    public UIButtonBlocker buttonBlocker;

    public bool debugValues;

    private void OnEnable()
    {
        if (!HapticManager.Instance.stimActive) { HapticManager.Instance.ToggleStim(); }
        //HapticManager.Instance.StartStim();
        HapticManager.Instance.ActivateEncoders(0, 1);
        Debug.Log("Activating calibration encoder");

        //Maybe set these to a toggle?
        //Let the user choose which calibration model they want to do?
        calFrequency = tapCalFrequency;
        PA_CalibrationPulseDuration = tapPA_CalibrationDuration;
        PW_CalibrationPulseDuration = tapPW_CalibrationDuration;
    }

    // Update is called once per frame
    void Update()
    {
        calibrationTitle.text = lateralMedial == 0 ? "Lateral Calibration Progress" : "Medial Calibration Progress";

        if (testPulseTimer > 0)
        {
            testPulseTimer -= Time.deltaTime;
        }
        else
        {
            if (sendingTestPulse)
            {
                sendingTestPulse = false;

                HapticManager.Instance.calibrationEncoderLateral.AddPoint(1, DateTime.Now);
                HapticManager.Instance.calibrationEncoderLateral.SetParameter(StimParam.PW, 0);

                HapticManager.Instance.calibrationEncoderMedial.AddPoint(1, DateTime.Now);
                HapticManager.Instance.calibrationEncoderMedial.SetParameter(StimParam.PW, 0);
            }
        }

        if (calibrationStep == 0) { calibrationMode = 0; minMax = 0; }
        else if (calibrationStep == 1) { calibrationMode = 1; minMax = 0; }
        else if (calibrationStep == 2) { calibrationMode = 0; minMax = 1; }
        else if (calibrationStep == 3) { calibrationMode = 1; minMax = 1; }

        SetStepVisuals(calibrationStep);
    }

    public void UpdateUserIntensityDiscrete(float direction)
    {
        //Setting user threshold and max PA with a high PW
        if (calibrationMode == 0)
        {
            double currentValue = minMax == 0 ? userPA_Threshold : userPA_Maximum;
            double newPA = currentValue + (direction < 0 ? -PA_Increment : PA_Increment);
            newPA = (double)Mathf.Clamp((float)newPA, 0, 10);

            if(lateralMedial == 0)
            {
                HapticManager.Instance.calibrationEncoderLateral.SetParameter(StimParam.PW, PA_CalibrationPW);
                HapticManager.Instance.calibrationEncoderLateral.SetParameter(StimParam.PA, newPA);
                HapticManager.Instance.calibrationEncoderLateral.SetParameter(StimParam.Freq, calFrequency);
                HapticManager.Instance.calibrationEncoderLateral.SetParameter(StimParam.Waveform, waveform);
                HapticManager.Instance.calibrationEncoderLateral.AddPoint(1, DateTime.Now);
            }
            else
            {
                HapticManager.Instance.calibrationEncoderMedial.SetParameter(StimParam.PW, PA_CalibrationPW);
                HapticManager.Instance.calibrationEncoderMedial.SetParameter(StimParam.PA, newPA);
                HapticManager.Instance.calibrationEncoderMedial.SetParameter(StimParam.Freq, calFrequency);
                HapticManager.Instance.calibrationEncoderMedial.SetParameter(StimParam.Waveform, waveform);
                HapticManager.Instance.calibrationEncoderMedial.AddPoint(1, DateTime.Now);
            }

            sendingTestPulse = true;
            testPulseTimer = PA_CalibrationPulseDuration;

            buttonBlocker.BlockButtonsForTime(PA_CalibrationPulseDuration);

            //Set PA threshold
            if (minMax == 0)
            {
                userPA_Threshold = Math.Round(newPA, 2);
                LogValues($"New user PA: {userPA_Threshold} Current user PW: {PA_CalibrationPW}");

                if(lateralMedial == 0)
                {
                    //HapticManager.Instance.lateralBounder.P0 = new StimPoint(userPA_Threshold, PA_CalibrationPW);
                    stimpoints[0] = new StimPoint(userPA_Threshold, PA_CalibrationPW);
                    HapticManager.Instance.lateralBounder.AddCalibrationData(Waveform.Sine.ToString(), calFrequency, stimpoints);
                }
                else
                {
                    //HapticManager.Instance.medialBounder.P0 = new StimPoint(userPA_Threshold, PA_CalibrationPW);
                    stimpoints[0] = new StimPoint(userPA_Threshold, PA_CalibrationPW);
                    HapticManager.Instance.medialBounder.AddCalibrationData(Waveform.Sine.ToString(), calFrequency, stimpoints);
                }  
            }
            //Set PA maximum
            else
            {
                userPA_Maximum = Math.Round(newPA, 2);
                LogValues($"New user PA: {userPA_Maximum} Current user PW: {PA_CalibrationPW}");

                if(lateralMedial == 0)
                {
                    //HapticManager.Instance.lateralBounder.P1 = new StimPoint(userPA_Maximum, PA_CalibrationPW);
                    stimpoints[1] = new StimPoint(userPA_Maximum, PA_CalibrationPW);
                    HapticManager.Instance.lateralBounder.AddCalibrationData(Waveform.Sine.ToString(), calFrequency, stimpoints);
                }
                else
                {
                    //HapticManager.Instance.medialBounder.P1 = new StimPoint(userPA_Maximum, PA_CalibrationPW);
                    stimpoints[1] = new StimPoint(userPA_Maximum, PA_CalibrationPW);
                    HapticManager.Instance.medialBounder.AddCalibrationData(Waveform.Sine.ToString(), calFrequency, stimpoints);
                }  
            }

            //HapticManager.Instance.lateralBounder.CalFreq = calFrequency;
            //HapticManager.Instance.medialBounder.CalFreq = calFrequency;
        }
        //Setting user threshold and max PW with a high PA
        else
        {
            double currentValue = minMax == 0 ? userPW_Threshold : userPW_Maximum;
            double newPW = currentValue + (direction < 0 ? -PW_Increment : PW_Increment);
            newPW = (double)Mathf.Clamp((float)newPW, 0, 255);

            if(lateralMedial == 0)
            {
                HapticManager.Instance.calibrationEncoderLateral.SetParameter(StimParam.PW, newPW);
                HapticManager.Instance.calibrationEncoderLateral.SetParameter(StimParam.PA, PW_CalibrationPA);
                HapticManager.Instance.calibrationEncoderLateral.SetParameter(StimParam.Freq, calFrequency);
                HapticManager.Instance.calibrationEncoderLateral.SetParameter(StimParam.Waveform, waveform);
                HapticManager.Instance.calibrationEncoderLateral.AddPoint(1, DateTime.Now);
            }
            else
            {
                HapticManager.Instance.calibrationEncoderMedial.SetParameter(StimParam.PW, newPW);
                HapticManager.Instance.calibrationEncoderMedial.SetParameter(StimParam.PA, PW_CalibrationPA);
                HapticManager.Instance.calibrationEncoderMedial.SetParameter(StimParam.Freq, calFrequency);
                HapticManager.Instance.calibrationEncoderMedial.SetParameter(StimParam.Waveform, waveform);
                HapticManager.Instance.calibrationEncoderMedial.AddPoint(1, DateTime.Now);
            }

            sendingTestPulse = true;
            testPulseTimer = PW_CalibrationPulseDuration;

            buttonBlocker.BlockButtonsForTime(PW_CalibrationPulseDuration);

            //Set PW threshold
            if (minMax == 0)
            {
                userPW_Threshold = Math.Round(newPW);
                LogValues($"New user PW: {userPW_Threshold} Current user PA: {PW_CalibrationPA}");

                if(lateralMedial == 0)
                {
                    //HapticManager.Instance.lateralBounder.P2 = new StimPoint(PW_CalibrationPA, userPW_Threshold);
                    stimpoints[2] = new StimPoint(PW_CalibrationPA, userPW_Threshold);
                    HapticManager.Instance.lateralBounder.AddCalibrationData(Waveform.Sine.ToString(), calFrequency, stimpoints);
                }
                else
                {
                    //HapticManager.Instance.medialBounder.P2 = new StimPoint(PW_CalibrationPA, userPW_Threshold);
                    stimpoints[2] = new StimPoint(PW_CalibrationPA, userPW_Threshold);
                    HapticManager.Instance.medialBounder.AddCalibrationData(Waveform.Sine.ToString(), calFrequency, stimpoints);
                }    
            }
            //Set PW maximum
            else
            {
                userPW_Maximum = Math.Round(newPW);
                LogValues($"New user PW: {userPW_Maximum} Current user PA: {PW_CalibrationPA}");

                if(lateralMedial == 0)
                {
                    //HapticManager.Instance.lateralBounder.P3 = new StimPoint(PW_CalibrationPA, userPW_Maximum);
                    stimpoints[3] = new StimPoint(PW_CalibrationPA, userPW_Maximum);
                    HapticManager.Instance.lateralBounder.AddCalibrationData(Waveform.Sine.ToString(), calFrequency, stimpoints);
                }
                else
                {
                    //HapticManager.Instance.medialBounder.P3 = new StimPoint(PW_CalibrationPA, userPW_Maximum);
                    stimpoints[3] = new StimPoint(PW_CalibrationPA, userPW_Maximum);
                    HapticManager.Instance.medialBounder.AddCalibrationData(Waveform.Sine.ToString(), calFrequency, stimpoints);
                } 
            }

            //HapticManager.Instance.lateralBounder.CalFreq = calFrequency;
            //HapticManager.Instance.medialBounder.CalFreq = calFrequency;
        }
    }

    public void TestUserPAThreshold()
    {
        if(lateralMedial == 0)
        {
            HapticManager.Instance.calibrationEncoderLateral.SetParameter(StimParam.PW, PA_CalibrationPW);
            HapticManager.Instance.calibrationEncoderLateral.SetParameter(StimParam.PA, userPA_Threshold);
            HapticManager.Instance.calibrationEncoderLateral.SetParameter(StimParam.Freq, calFrequency);
            HapticManager.Instance.calibrationEncoderLateral.SetParameter(StimParam.Waveform, waveform);
            HapticManager.Instance.calibrationEncoderLateral.AddPoint(1, DateTime.Now);
        }
        else
        {
            HapticManager.Instance.calibrationEncoderMedial.SetParameter(StimParam.PW, PA_CalibrationPW);
            HapticManager.Instance.calibrationEncoderMedial.SetParameter(StimParam.PA, userPA_Threshold);
            HapticManager.Instance.calibrationEncoderMedial.SetParameter(StimParam.Freq, calFrequency);
            HapticManager.Instance.calibrationEncoderMedial.SetParameter(StimParam.Waveform, waveform);
            HapticManager.Instance.calibrationEncoderMedial.AddPoint(1, DateTime.Now);
        }

        sendingTestPulse = true;
        testPulseTimer = PA_CalibrationPulseDuration;
    }

    public void TestUserPAMaximum()
    {
        if (lateralMedial == 0)
        {
            HapticManager.Instance.calibrationEncoderLateral.SetParameter(StimParam.PW, PA_CalibrationPW);
            HapticManager.Instance.calibrationEncoderLateral.SetParameter(StimParam.PA, userPA_Maximum);
            HapticManager.Instance.calibrationEncoderLateral.SetParameter(StimParam.Freq, calFrequency);
            HapticManager.Instance.calibrationEncoderLateral.SetParameter(StimParam.Waveform, waveform);
            HapticManager.Instance.calibrationEncoderLateral.AddPoint(1, DateTime.Now);
        }
        else
        {
            HapticManager.Instance.calibrationEncoderMedial.SetParameter(StimParam.PW, PA_CalibrationPW);
            HapticManager.Instance.calibrationEncoderMedial.SetParameter(StimParam.PA, userPA_Maximum);
            HapticManager.Instance.calibrationEncoderMedial.SetParameter(StimParam.Freq, calFrequency);
            HapticManager.Instance.calibrationEncoderMedial.SetParameter(StimParam.Waveform, waveform);
            HapticManager.Instance.calibrationEncoderMedial.AddPoint(1, DateTime.Now);
        }

        sendingTestPulse = true;
        testPulseTimer = PA_CalibrationPulseDuration;
    }

    public void TestUserPWThreshold()
    {
        if (lateralMedial == 0)
        {
            HapticManager.Instance.calibrationEncoderLateral.SetParameter(StimParam.PW, userPW_Threshold);
            HapticManager.Instance.calibrationEncoderLateral.SetParameter(StimParam.PA, PW_CalibrationPA);
            HapticManager.Instance.calibrationEncoderLateral.SetParameter(StimParam.Freq, calFrequency);
            HapticManager.Instance.calibrationEncoderLateral.SetParameter(StimParam.Waveform, waveform);
            HapticManager.Instance.calibrationEncoderLateral.AddPoint(1, DateTime.Now);
        }
        else
        {
            HapticManager.Instance.calibrationEncoderMedial.SetParameter(StimParam.PW, userPW_Threshold);
            HapticManager.Instance.calibrationEncoderMedial.SetParameter(StimParam.PA, PW_CalibrationPA);
            HapticManager.Instance.calibrationEncoderMedial.SetParameter(StimParam.Freq, calFrequency);
            HapticManager.Instance.calibrationEncoderMedial.SetParameter(StimParam.Waveform, waveform);
            HapticManager.Instance.calibrationEncoderMedial.AddPoint(1, DateTime.Now);
        }

        sendingTestPulse = true;
        testPulseTimer = PW_CalibrationPulseDuration;
    }

    public void TestUserPWMaximum()
    {
        if (lateralMedial == 0)
        {
            HapticManager.Instance.calibrationEncoderLateral.SetParameter(StimParam.PW, userPW_Maximum);
            HapticManager.Instance.calibrationEncoderLateral.SetParameter(StimParam.PA, PW_CalibrationPA);
            HapticManager.Instance.calibrationEncoderLateral.SetParameter(StimParam.Freq, calFrequency);
            HapticManager.Instance.calibrationEncoderLateral.SetParameter(StimParam.Waveform, waveform);
            HapticManager.Instance.calibrationEncoderLateral.AddPoint(1, DateTime.Now);
        }
        else
        {
            HapticManager.Instance.calibrationEncoderMedial.SetParameter(StimParam.PW, userPW_Maximum);
            HapticManager.Instance.calibrationEncoderMedial.SetParameter(StimParam.PA, PW_CalibrationPA);
            HapticManager.Instance.calibrationEncoderMedial.SetParameter(StimParam.Freq, calFrequency);
            HapticManager.Instance.calibrationEncoderMedial.SetParameter(StimParam.Waveform, waveform);
            HapticManager.Instance.calibrationEncoderMedial.AddPoint(1, DateTime.Now);
        }

        sendingTestPulse = true;
        testPulseTimer = PW_CalibrationPulseDuration;
    }

    public void CalibrationSettingConfirmation()
    {
        if (calibrationStep < 3)
        {
            calibrationStep++;
            LogValues($"Calibration step {calibrationStep}");
            if (calibrationStep == 2) { userPA_Maximum = userPA_Threshold; }
            else if (calibrationStep == 3) { userPW_Maximum = userPW_Threshold; }
        }
        else
        {
            if (lateralMedial == 0)
            {
                lateralMedial = 1;
                calibrationStep = 0;
                userPA_Threshold = 0;
                userPA_Maximum = 0;
                userPW_Threshold = 0;
                userPW_Maximum = 0;
                SetStepVisuals(0);
            }
            else
            {
                LogValues("Starting Quality Encoder");
                calibrationComplete = true;
                User currentUser = HapticManager.Instance.currentUser;
                //Flipped the name due to Dustin's saving in User class
                string userFile = $"{currentUser.Name.Last}{currentUser.Name.Middle}{currentUser.Name.First}";
                string targetPath = Path.Combine(usersDir, userFile + ".json");
                currentUser.SaveUserData(targetPath);
                calibrationCompleteEvent?.Invoke();
                //HapticManager.Instance.StopStim();
                HapticManager.Instance.ActivateEncoders(2, 3);
            }
        }
    }

    void SetStepVisuals(int step)
    {
        for (int i = 0; i < stepHighlights.Length; i++)
        {
            stepHighlights[i].enabled = i == step ? true : false;

            checkMarks[i].SetActive(i < step || calibrationComplete);
        }
    }

    public void ResetCalibration()
    {
        userPA_Threshold = 0;
        userPA_Maximum = 0;
        userPW_Threshold = 0;
        userPW_Maximum = 0;
        calibrationStep = 0;
        lateralMedial = 0;
        calibrationComplete = false;
    }

    void LogValues(string message)
    {
        if (debugValues)
        {
            Debug.Log(message);
        }
    }
}
