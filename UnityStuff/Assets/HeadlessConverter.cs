using UnityEngine;
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
    [Tooltip("The full URL of your Flask endpoint")]
    public string flaskEndpointUrl = "https://backend-api-141904499148.us-central1.run.app/assist";
    [Tooltip("The API key for your backend service")]
    public string apiKey = "my-super-secret-key-12345"; // Set this in the Inspector

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

    // --- We no longer need the 'Controller Input' header or 'aButtonAction' ---
    [SerializeField] private TMP_Text outPutResponse;
    private bool isRequestPending = false;
    private string sessionId = "";

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
        TakeSnapshotAndUpload();

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

        // OPTIONAL: If you also want the 'X' button on the left controller:
        // if (OVRInput.GetDown(OVRInput.Button.Three))
        // {
        //     Log("'X' button pressed. Taking snapshot...");
        //     TakeSnapshotAndUpload();
        // }
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
        using (UnityWebRequest www = UnityWebRequest.Post(flaskEndpointUrl, form))
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
                    Log("Response: " + responseText);
                    outPutResponse.text = responseText;

                    // You can parse the JSON here to extract instruction_steps, target_id, haptic_cue
                    // Example: AssistResponse response = JsonUtility.FromJson<AssistResponse>(responseText);
                }
                catch (Exception e)
                {
                    Log("Error parsing response: " + e.Message);
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

    // Helper for logging to UI and Console
    void Log(string message)
    {
        Debug.Log("[HeadlessConverter] " + message);
        if (debugStatusText != null)
        {
            debugStatusText.text = message;
        }
    }
}

// Optional: Response data structure for parsing JSON response
[System.Serializable]
public class AssistResponse
{
    public string status;
    public string session_id;
    public string instruction_id;
    public string[] instruction_steps;
    public string target_id;
    public string haptic_cue;
}