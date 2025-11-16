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

    // --- We no longer need the ImagePayload class ---

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

        // --- 3. Encode the Texture2D Directly to JPG ---
        byte[] jpgData = sourceTexture.EncodeToJPG(90); // 90% quality

        if (jpgData == null)
        {
            Log("Error: EncodeToJPG failed.");
            return;
        }

        Log("Encoding complete. Starting upload coroutine...");

        // --- 4. Start the Asynchronous Upload with the raw JPG bytes ---
        StartCoroutine(UploadJPG(jpgData));
    }

    /// <summary>
    /// This Coroutine sends the raw JPG byte array
    /// to the Flask server without blocking the main game thread.
    /// </summary>
    IEnumerator UploadJPG(byte[] jpgData)
    {
        isRequestPending = true;

        // --- MODIFIED SECTION ---
        // We don't create JSON. We send the raw bytes directly.
        using (UnityWebRequest www = new UnityWebRequest(flaskEndpointUrl, "POST"))
        {
            // Set the upload handler to our raw JPG data
            www.uploadHandler = new UploadHandlerRaw(jpgData);
            www.downloadHandler = new DownloadHandlerBuffer();

            // Set the content type header so Flask knows to expect a JPG
            www.SetRequestHeader("Content-Type", "image/jpeg");

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