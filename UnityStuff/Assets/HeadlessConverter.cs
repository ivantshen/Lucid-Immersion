using UnityEngine;
using System.IO;
using System;                // Required for Base64 conversion
using System.Text;           // REQUIRED for encoding JSON
using System.Collections;    // Required for Coroutines
using UnityEngine.Networking; // REQUIRED for network requests
using Meta.XR;    // Required for PassthroughCameraAccess
using TMPro;                 // Optional: for debug text

// This script now assumes you have an OVRPermissionsRequester
// component somewhere in your scene to handle the permission pop-up.

public class HeadlessConverter : MonoBehaviour
{
    [Header("Required Component")]
    [Tooltip("The MRUK PassthroughCameraAccess component in your scene")]
    public PassthroughCameraAccess passthroughAccess;

    [Header("Networking")]
    [Tooltip("The full URL of your Flask endpoint")]
    public string flaskEndpointUrl = "http://YOUR_SERVER_IP:5000/assist";

    [Header("Optional Debugging")]
    [Tooltip("Optional: A text element for status updates")]
    public TMP_Text debugStatusText;

    private bool isRequestPending = false;

    // A simple class to format our JSON payload
    [System.Serializable]
    private class ImagePayload
    {
        public string image;
    }

    /// <summary>
    /// Call this from your button's OnClick event.
    /// </summary>
    public void TakeSnapshotAndUpload()
    {
        // Prevent multiple requests
        if (isRequestPending)
        {
            Log("Cannot start new request; one is already pending.");
            return;
        }

        // --- 1. Run All Safety Checks ---
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

        // --- 2. Get the Texture (The Simple Way) ---
        // GetTexture() returns the Texture2D that the component is already managing.
        // We just need to cast it from Texture to Texture2D.
        Texture2D sourceTexture = passthroughAccess.GetTexture() as Texture2D;

        if (sourceTexture == null)
        {
            Log("Error: Failed to get Texture2D from PassthroughAccess.");
            return;
        }

        Log("Got texture. Encoding to JPG...");

        // --- 3. Encode the Texture2D Directly to JPG ---
        // This is a synchronous call, but it's fast.
        // It works because the PassthroughCameraAccess script
        // already put the pixel data on the CPU.
        byte[] jpgData = sourceTexture.EncodeToJPG(90); // 90% quality

        if (jpgData == null)
        {
            Log("Error: EncodeToJPG failed.");
            return;
        }

        Log("Encoding to Base64...");

        // --- 4. Convert JPG data to Base64 String ---
        string base64String = Convert.ToBase64String(jpgData);

        Log("Starting upload coroutine...");

        // --- 5. Start the Asynchronous Upload ---
        StartCoroutine(UploadBase64Image(base64String));
    }

    IEnumerator UploadBase64Image(string base64String)
    {
        isRequestPending = true;

        ImagePayload payload = new ImagePayload { image = base64String };
        string jsonPayload = JsonUtility.ToJson(payload);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);

        using (UnityWebRequest www = new UnityWebRequest(flaskEndpointUrl, "POST"))
        {
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");

            Log("Sending data to server...");
            yield return www.SendWebRequest();

            isRequestPending = false;

            if (www.result == UnityWebRequest.Result.Success)
            {
                Log("SUCCESS: Server responded!");
                Log(www.downloadHandler.text);
            }
            else
            {
                Log("ERROR: " + www.error);
            }
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