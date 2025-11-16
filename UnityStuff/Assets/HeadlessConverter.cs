using UnityEngine;
using System.IO;
using System;
using System.Text;
using System.Collections;
using UnityEngine.Networking;
using Meta.XR; // This was the one you were missing
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

    [Header("Optional Debugging")]
    [Tooltip("Optional: A text element for status updates")]
    public TMP_Text debugStatusText;

    private bool isRequestPending = false;
    private string sessionId = "";

    void Start()
    {
        // Generate a session ID when the app starts
        sessionId = System.Guid.NewGuid().ToString();
        Log($"Session ID: {sessionId}");
    }

    /// <summary>
    /// Call this from your button's OnClick event.
    /// </summary>
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
        form.AddField("task_step", taskStep);
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

                    // You can parse the JSON here to extract instruction_steps, target_id, haptic_cue
                    // Example: JsonUtility.FromJson<ResponseData>(responseText);
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
            return $"{{\"x\": {gazeDirection.x}, \"y\": {gazeDirection.y}, \"z\": {gazeDirection.z}}}";
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