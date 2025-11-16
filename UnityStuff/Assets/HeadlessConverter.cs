using UnityEngine;
using UnityEngine.Android;
using System.IO;
using System;
using System.Text;
using System.Collections;
using UnityEngine.Networking;
using Meta.XR;
using TMPro;

// This script now assumes you have an OVRPermissionsRequester
// component somewhere in your scene to handle the permission pop-up.

public class HeadlessConverter : MonoBehaviour
{
    [Header("Required Component")]
    [Tooltip("The MRUK PassthroughCameraAccess component in your scene")]
    public PassthroughCameraAccess passthroughAccess;

    [Header("Networking")]
    [Tooltip("The full URL of your Flask /assist endpoint")]
    public string assistEndpointUrl = "https://backend-api-141904499148.us-central1.run.app/assist";
    [Tooltip("The full URL of your Flask /ask endpoint")]
    public string askEndpointUrl = "https://backend-api-141904499148.us-central1.run.app/ask";
    [Tooltip("The API key for your backend service")]
    public string apiKey = "my-super-secret-key-12345"; // Set this in the Inspector

    [Header("UI Display")]
    [Tooltip("Text element to display the image analysis (header)")]
    public TMP_Text headerText;

    [Tooltip("Text element to display timestamp and step (subheader)")]
    public TMP_Text subheaderText;

    [Tooltip("Text element to display instruction steps (main content)")]
    public TMP_Text instructionText;

    [Header("Optional Debugging")]
    [Tooltip("Optional: A text element for status updates")]
    public TMP_Text debugStatusText;

    [Header("Task Context")]
    [Tooltip("The current high-level task the user is performing")]
    public string currentTask = "default_task"; // You can change this
    [Tooltip("The current step number within the task")]
    public int taskStep = 0; // You can change this

    [Header("Gaze Tracking")]
    [Tooltip("Enable sending gaze data to the server")]
    public bool sendGazeData = true;
    [Tooltip("Assign the OVRCameraRig's CenterEyeAnchor here")]
    public Transform centerEyeAnchor; // Assign this in the Inspector

    //Audio variables
    private AudioClip recording;
    private bool isRecording = false;
    private const int MAX_RECORD_TIME_SEC = 10;
    private const int SAMPLE_RATE = 44100;

    private bool isRequestPending = false;
    private string sessionId = "";
    private string lastSessionId = ""; // Store session ID from /assist for use with /ask

    // A simple class to format our JSON payload
    [System.Serializable]
    private class ImagePayload
    {
        public string image;
    }

    void Start()
    {
        // Generate a session ID when the app starts
        sessionId = System.Guid.NewGuid().ToString();
        Log($"Session ID: {sessionId}");

        // --- RECOMMENDED ---
        // Auto-find the CenterEyeAnchor if it's not set
        if (sendGazeData && centerEyeAnchor == null)
        {
            try
            {
                // Note: This requires the OVRCameraRig to be in the scene
                centerEyeAnchor = FindObjectOfType<OVRCameraRig>().centerEyeAnchor;
                if (centerEyeAnchor != null)
                {
                    Log("Automatically found CenterEyeAnchor.");
                }
                else
                {
                    Log("Could not find CenterEyeAnchor. Disabling gaze data.");
                    sendGazeData = false;
                }
            }
            catch (Exception e)
            {
                Log($"Error finding CenterEyeAnchor: {e.Message}. Disabling gaze data.");
                sendGazeData = false;
            }
        }
        string microphonePermission = "android.permission.RECORD_AUDIO";

        // Check for permission using the Android.Permission class
        if (!Permission.HasUserAuthorizedPermission(microphonePermission))
        {
            Log("Microphone permission not granted. Requesting...");
            // Make sure your OVRPermissionsRequester component in the scene
            // is set up to request this permission!
        }
        else
        {
            Log("Microphone permission is already granted.");
        }

        // --- END RECOMMENDED ---
    }

    /// <summary>
    /// This Update function now checks for the 'A' button press
    /// using OVRInput from the Meta XR Core SDK.
    /// </summary>
    void Update()
    {
        // Check if the 'A' button (aliased as Button.One) on the right controller
        // was pressed down this frame.
        if (OVRInput.GetDown(OVRInput.Button.One))
        {
            Log("'A' button pressed. Taking snapshot...");
            TakeSnapshotAndUpload();
        }

        // --- "B" Button for Audio Recording ---
        // B button is 'OVRInput.Button.Two'

        // Start recording when B is pressed
        if (OVRInput.GetDown(OVRInput.Button.Two) && !isRecording)
        {
            StartRecording();
        }

        // Stop recording when B is released
        if (OVRInput.GetUp(OVRInput.Button.Two) && isRecording)
        {
            StopAndUploadRecording();
        }
    }

    public void TakeSnapshotAndUpload()
    {
        if (isRequestPending)
        {
            Log("Cannot start new request; one is already pending.");
            return;
        }

        if (passthroughAccess == null)
        {
            Log("Error: PassthroughCameraAccess component is not assigned!");
            return;
        }
        if (!passthroughAccess.IsPlaying)
        {
            Log("Error: Camera is not playing. Check permissions or if component is enabled.");
            return;
        }

        Log("Checks passed. Getting texture...");

        Texture2D sourceTexture = passthroughAccess.GetTexture() as Texture2D;

        if (sourceTexture == null)
        {
            Log("Error: Failed to get Texture2D from PassthroughAccess.");
            return;
        }

        Log("Got texture. Encoding to JPG...");

        // Encode the Texture2D to JPG
        byte[] jpgData = sourceTexture.EncodeToJPG(90); // 90% quality

        if (jpgData == null)
        {
            Log("Error: EncodeToJPG failed.");
            return;
        }

        Log("Encoding complete. Starting upload coroutine...");

        // Start the upload with multipart/form-data
        StartCoroutine(UploadMultipartFormData(jpgData));
    }

    /// <summary>
    /// This Coroutine sends the image and metadata as multipart/form-data
    /// to the Flask server, matching the backend API requirements.
    /// </summary>
    IEnumerator UploadMultipartFormData(byte[] jpgData)
    {
        isRequestPending = true;

        // Create multipart form data
        WWWForm form = new WWWForm();

        // Add the image file
        form.AddBinaryData("snapshot", jpgData, "snapshot.jpg", "image/jpeg");

        // Add task context fields
        form.AddField("task_step", taskStep.ToString());
        form.AddField("current_task", currentTask);
        form.AddField("session_id", sessionId);

        // Add gaze vector data
        string gazeVectorJson = GetGazeVectorJson();
        form.AddField("gaze_vector", gazeVectorJson);

        // Create the request
        using (UnityWebRequest www = UnityWebRequest.Post(assistEndpointUrl, form))
        {
            // Add Authorization header
            www.SetRequestHeader("Authorization", "Bearer " + apiKey);

            Log("Sending data to server...");
            Log($"Task: {currentTask}, Step: {taskStep}");

            yield return www.SendWebRequest();

            isRequestPending = false;

            if (www.result == UnityWebRequest.Result.Success)
            {
                Log("SUCCESS: Server responded!");

                // Parse the JSON response
                try
                {
                    string responseText = www.downloadHandler.text;
                    Log("Raw response: " + responseText);

                    AssistResponse response = JsonUtility.FromJson<AssistResponse>(responseText);

                    // Store the session ID for future /ask requests
                    lastSessionId = response.session_id;
                    Log($"Stored session ID: {lastSessionId}");

                    // Display the structured response
                    DisplayStructuredResponse(response, response.image_analysis, response.timestamp);
                }
                catch (Exception e)
                {
                    Log("Error parsing response: " + e.Message);
                    DisplayError("Failed to parse server response: " + e.Message);
                }
            }
            else
            {
                Log("ERROR: " + www.error);
                if (www.downloadHandler != null)
                {
                    Log("Error details: " + www.downloadHandler.text);
                }
            }
        }
    }

    /// <summary>
    /// Get the gaze vector as a JSON string.
    /// Returns a default vector if gaze tracking is disabled or centerEyeAnchor is not set.
    /// </summary>
    string GetGazeVectorJson()
    {
        if (sendGazeData && centerEyeAnchor != null)
        {
            Vector3 gazeDirection = centerEyeAnchor.forward;
            // Using InvariantCulture for consistent decimal formatting
            return $"{{\"x\": {gazeDirection.x.ToString(System.Globalization.CultureInfo.InvariantCulture)}, \"y\": {gazeDirection.y.ToString(System.Globalization.CultureInfo.InvariantCulture)}, \"z\": {gazeDirection.z.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}";
        }
        else
        {
            // Return a default forward vector
            return "{\"x\": 0, \"y\": 0, \"z\": 1}";
        }
    }

    // AUDIO METHODS
    void StartRecording()
    {
        if (Microphone.devices.Length == 0)
        {
            Log("No microphone found!");
            return;
        }

        Log("Starting audio recording...");
        isRecording = true;
        // Start recording from the default mic, loop for MAX_RECORD_TIME_SEC, at SAMPLE_RATE
        recording = Microphone.Start(null, true, MAX_RECORD_TIME_SEC, SAMPLE_RATE);
    }

    void StopAndUploadRecording()
    {
        Log("Stopping audio recording...");
        isRecording = false;

        // Stop the microphone
        Microphone.End(null); // 'null' for the default mic

        // Use our WavUtility to convert the AudioClip to a .wav byte array
        byte[] wavData = WavUtility.FromAudioClip(recording);

        if (wavData == null)
        {
            Log("Failed to create .wav data.");
            return;
        }

        Log("Audio converted to .wav. Starting upload...");
        // Start a new coroutine to upload the audio
        StartCoroutine(UploadAudioData(wavData));
    }

    IEnumerator UploadAudioData(byte[] audioData)
    {
        // Check if we have a session ID from a previous /assist call
        if (string.IsNullOrEmpty(lastSessionId))
        {
            Log("ERROR: No session ID available. Please take a snapshot first (press A button).");
            DisplayError("No active session. Take a snapshot first before asking questions.");
            yield break;
        }

        isRequestPending = true;

        WWWForm form = new WWWForm();

        // Add the audio file for the /ask endpoint
        form.AddBinaryData("audio", audioData, "recording.wav", "audio/wav");

        // Add the session_id from the previous /assist call
        form.AddField("session_id", lastSessionId);

        // Create the request to the /ask endpoint
        using (UnityWebRequest www = UnityWebRequest.Post(askEndpointUrl, form))
        {
            www.SetRequestHeader("Authorization", "Bearer " + apiKey);

            Log($"Sending audio question to /ask endpoint for session: {lastSessionId}");
            yield return www.SendWebRequest();

            isRequestPending = false;

            if (www.result == UnityWebRequest.Result.Success)
            {
                Log("SUCCESS: Audio question processed. Server responded!");

                // Parse the JSON response from /ask endpoint
                try
                {
                    string responseText = www.downloadHandler.text;
                    Log("Raw response: " + responseText);

                    AskResponse response = JsonUtility.FromJson<AskResponse>(responseText);

                    // Display the transcribed question and answer
                    DisplayAskResponse(response);
                }
                catch (Exception e)
                {
                    Log("Error parsing /ask response: " + e.Message);
                    DisplayError("Failed to parse server response: " + e.Message);
                }
            }
            else
            {
                Log("ERROR: " + www.error);
                if (www.downloadHandler != null)
                {
                    Log("Error details: " + www.downloadHandler.text);
                }
                DisplayError($"Audio question failed: {www.error}");
            }
        }
    }

    /// <summary>
    /// Display the response in a structured format
    /// Header: Image Analysis
    /// Subheader: Timestamp and Step
    /// Content: Instruction steps as a list
    /// </summary>
    void DisplayStructuredResponse(AssistResponse response, string imageAnalysis, string timestamp)
    {
        // Header: Image Analysis
        if (headerText != null)
        {
            headerText.text = imageAnalysis;
        }

        // Subheader: Timestamp and Step
        if (subheaderText != null)
        {
            string stepInfo = $"Step {response.instruction_id.Split('-')[^1]} | {FormatTimestamp(timestamp)}";
            subheaderText.text = stepInfo;
        }

        // Main Content: Instruction Steps as a numbered list
        if (instructionText != null)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Instructions:");
            sb.AppendLine();

            for (int i = 0; i < response.instruction_steps.Length; i++)
            {
                sb.AppendLine($"{i + 1}. {response.instruction_steps[i]}");
                if (i < response.instruction_steps.Length - 1)
                {
                    sb.AppendLine();
                }
            }

            instructionText.text = sb.ToString();
        }

        Log("Response displayed successfully");
    }

    /// <summary>
    /// Display error message in the UI
    /// </summary>
    void DisplayError(string errorMessage)
    {
        if (headerText != null)
        {
            headerText.text = "Error";
        }

        if (subheaderText != null)
        {
            subheaderText.text = DateTime.Now.ToString("HH:mm:ss");
        }

        if (instructionText != null)
        {
            instructionText.text = errorMessage;
        }
    }

    /// <summary>
    /// Format timestamp to a readable format
    /// </summary>
    string FormatTimestamp(string isoTimestamp)
    {
        try
        {
            DateTime dt = DateTime.Parse(isoTimestamp);
            return dt.ToString("MMM dd, HH:mm:ss");
        }
        catch
        {
            return DateTime.Now.ToString("MMM dd, HH:mm:ss");
        }
    }

    // Helper for logging to UI and Console
    void Log(string message)
    {
        Debug.Log("[HeadlessConverter] " + message);
        if (debugStatusText != null)
        {
            debugStatusText.text = message;
        }
    }

    /// <summary>
    /// Display the response from /ask endpoint (voice question)
    /// Header: Transcribed Question
    /// Subheader: Task context
    /// Content: Answer steps as a list
    /// </summary>
    void DisplayAskResponse(AskResponse response)
    {
        // Header: Show the transcribed question
        if (headerText != null)
        {
            string questionText = response.transcribed_question ?? "Voice Question";
            headerText.text = $"Q: {questionText}";
        }

        // Subheader: Show task context
        if (subheaderText != null)
        {
            string contextInfo = $"Task: {response.context.task} | Step: {response.context.step}";
            subheaderText.text = contextInfo;
        }

        // Main Content: Answer steps as a numbered list
        if (instructionText != null)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Answer:");
            sb.AppendLine();

            for (int i = 0; i < response.answer_steps.Length; i++)
            {
                sb.AppendLine($"{i + 1}. {response.answer_steps[i]}");
                if (i < response.answer_steps.Length - 1)
                {
                    sb.AppendLine();
                }
            }

            instructionText.text = sb.ToString();
        }

        Log("Voice question response displayed successfully");
    }
}

// Response data structure for parsing JSON response from /assist endpoint
[System.Serializable]
public class AssistResponse
{
    public string status;
    public string session_id;
    public string instruction_id;
    public string[] instruction_steps;
    public string target_id;
    public string haptic_cue;
    public string image_analysis;
    public string timestamp;
}

// Response data structure for parsing JSON response from /ask endpoint
[System.Serializable]
public class AskResponse
{
    public string status;
    public string session_id;
    public string[] answer_steps;
    public string transcribed_question;
    public AskContext context;
}

[System.Serializable]
public class AskContext
{
    public string task;
    public string step;
    public string previous_instruction;
}