using Afference.HWInt;
using AfferenceEngine.src.Core.Entities;
using AfferenceEngine.src.Core.Interfaces;
using AfferenceEngine.src.Core.Managers;
using AfferenceEngine.src.Core.Other;
using AfferenceEngine.src.Core.StimulationLogic;
using AfferenceEngine.src.Implementations.Products;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

public class HapticManager : MonoBehaviour
{
    public static HapticManager Instance;

    private HapticFactory hf;
    private HapticSession hs;
    public User currentUser;
    [HideInInspector]
    public float userVolume = 0f;
    private float volChangeStep = .1f;
    string UserDirectory => Path.Combine(Application.persistentDataPath, "Users");
    string existingUserPath;
    private string commType, port;

    public PulseParamManager lateralBounder, medialBounder;
    public List<IQualityEncoder> qualityEncoders = new List<IQualityEncoder>();
    public List<IQualityEncoder> activeEncoders = new List<IQualityEncoder>();
    public IQualityEncoder calibrationEncoderLateral, calibrationEncoderMedial;
    private int? _activeLatIndex;
    private int? _activeMedIndex;

    [HideInInspector] public bool stimActive;
    [HideInInspector] public string status = "";

    private Transport transport;
    private CancellationTokenSource _cts;
    private bool isShuttingDown;


    //Cache burst encoders as we load them
    private readonly Dictionary<Guid, List<string>> _burstTrainNames = new();
    public IReadOnlyList<string> GetBurstTrainNames(Guid encoderId) =>
        _burstTrainNames.TryGetValue(encoderId, out var list) ? list : Array.Empty<string>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        DontDestroyOnLoad(gameObject);
        Instance = this;

        Application.runInBackground = true;            // keep Unity alive during BLE init
        Screen.sleepTimeout = SleepTimeout.NeverSleep; // avoid dozing in handshake
        new CopyAssets().CopyAllAssets();
        Application.targetFrameRate = 0;

#if UNITY_ANDROID && !UNITY_EDITOR
        SetCommType("ble");
#endif
    }

    public void SetCommType(string type) => commType = type;
    public void SetPort(string portName) => port = portName;
    public void SetDevice(string deviceName) => port = $"Afference SBRing-{deviceName.Trim()}";

    public void LoadUser(string fileName)
    {
        existingUserPath = Path.Combine(UserDirectory, fileName + ".json");
        Debug.Log($"Loading user: {existingUserPath}");
        //currentUser = new User().LoadUserData(existingUserPath);
        var user = new User();
        user.LoadUserData(existingUserPath);
        currentUser = user;
        userVolume = (float)currentUser.UserVolume;
    }


#if UNITY_ANDROID && !UNITY_EDITOR
private TaskCompletionSource<bool> blePermissionTcs;
private string[] blePermissions;

public async Task<bool> EnsureBlePermissionsAsync(int timeoutMs = 10000)
{
    int sdk = new AndroidJavaClass("android.os.Build$VERSION").GetStatic<int>("SDK_INT");
    blePermissions = (sdk >= 31)
        ? new string[] { "android.permission.BLUETOOTH_SCAN", "android.permission.BLUETOOTH_CONNECT" }
        : new string[] { Permission.FineLocation };

    // Check if already granted
    bool allGranted = true;
    foreach (var p in blePermissions)
        if (!Permission.HasUserAuthorizedPermission(p)) { allGranted = false; break; }
    if (allGranted) return true;

    // Create TCS
    blePermissionTcs = new TaskCompletionSource<bool>();

    // Request permissions
    var pcb = new PermissionCallbacks();
    pcb.PermissionGranted += _ => ResolvePermissions();
    pcb.PermissionDenied += _ => ResolvePermissions();
    pcb.PermissionDeniedAndDontAskAgain += _ => ResolvePermissions();

    Permission.RequestUserPermissions(blePermissions, pcb);

    // Timeout fallback
    using var cts = new CancellationTokenSource(timeoutMs);
    cts.Token.Register(() =>
    {
        if (blePermissionTcs != null && !blePermissionTcs.Task.IsCompleted)
            blePermissionTcs.TrySetResult(false);
    });

    // Await result
    return await blePermissionTcs.Task;
}

private void ResolvePermissions()
{
    if (blePermissionTcs == null || blePermissionTcs.Task.IsCompleted) return;

    foreach (var p in blePermissions)
        if (!Permission.HasUserAuthorizedPermission(p))
        {
            blePermissionTcs.TrySetResult(false);
            return;
        }

    blePermissionTcs.TrySetResult(true);
}

public async Task WaitForAndroidFocusAndStabilityAsync(int postFocusDelayMs = 300)
{
    // Wait until the permission dialog is gone and app is foregrounded again.
    while (!Application.isFocused)
        await System.Threading.Tasks.Task.Yield();

    // Give the platform a breath to settle Bluetooth state (Gatt, callbacks).
    await System.Threading.Tasks.Task.Delay(postFocusDelayMs);
}

#endif



    // Removed perAttemptTimeoutMs (it’s unused with the minimal bridge/transport).
    public async Task<bool> ConnectCurrentUserAsync(
        int maxAttempts = 5,
        float initialDelaySeconds = 1.0f)
    {
        if (currentUser == null) { Debug.LogError("No user loaded."); return false; }
        if (string.IsNullOrEmpty(commType)) { Debug.LogError("commType not set"); return false; }
        if (string.IsNullOrEmpty(port)) { Debug.LogError("port/device not set"); return false; }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        status = "Attempting connection...";
        Exception lastError = null;

#if UNITY_ANDROID && !UNITY_EDITOR
        AfferenceRingAndroidTransport native = null;
        Transport transport = null;

        try
        {
            native = new AfferenceRingAndroidTransport(openTimeoutMs: 12_000);
            transport = new Transport(native, commType, port);
            await Task.Delay(600); // small settle; CCCD/MTU complete
        }
        catch (Exception ex)
        {
            Debug.LogError($"[HapticManager] Initial transport open failed: {ex}");
            try { transport?.Dispose(); } catch { }
            return false;
        }

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (native == null || transport == null)
                {
                    try { transport?.Dispose(); } catch { }
                    native = new AfferenceRingAndroidTransport(openTimeoutMs: 12_000);
                    transport = new Transport(native, commType, port);
                    await Task.Delay(600);
                }

                var ring = new AfferenceRing(transport, currentUser, digit: 2);
                CreateSession(currentUser, ring);

                status = "Connected!";
                return true;
            }
            catch (OperationCanceledException)
            {
                status = "Connection canceled";
                return false;
            }
            catch (Exception ex)
            {
                lastError = ex;
                Debug.LogError($"[HapticManager] Attempt {attempt}/{maxAttempts} failed: {ex}");

                // With the minimal bridge, just rebuild the transport on each failure.
                try { transport?.Dispose(); } catch { }
                native = null;
                transport = null;

                if (attempt < maxAttempts)
                {
                    float wait = initialDelaySeconds * Mathf.Pow(1.5f, attempt - 1);
                    status = $"Retrying... ({attempt + 1}/{maxAttempts})";
                    try { await Task.Delay(TimeSpan.FromSeconds(wait), token); }
                    catch (OperationCanceledException) { return false; }
                }
            }
        }

        try { transport?.Dispose(); } catch { }
        if (lastError != null) Debug.LogException(lastError);
        status = "Could not connect to the Afference Ring.\nPlease restart the app.";
        return false;

#else
        // non-Android path
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var native = new NativeWindowsTransport();
                transport = new Transport(native, commType, port);
                var ring = new AfferenceRing(transport, currentUser, digit: 2);
                CreateSession(currentUser, ring);
                status = "Connected!";
                return true;
            }
            catch (Exception ex)
            {
                lastError = ex;
                Debug.LogError($"[HapticManager] Attempt {attempt}/{maxAttempts} failed: {ex}");
                try { transport?.Dispose(); } catch { }
                transport = null;

                if (attempt < maxAttempts)
                {
                    float wait = initialDelaySeconds * Mathf.Pow(1.5f, attempt - 1);
                    status = $"Retrying... ({attempt + 1}/{maxAttempts})";
                    try { await Task.Delay(TimeSpan.FromSeconds(wait), token); }
                    catch (OperationCanceledException) { return false; }
                }
            }
        }

        if (lastError != null) Debug.LogException(lastError);
        status = "Could not connect to the Afference Ring.\nPlease restart the app.";
        return false;
#endif
    }

    public void CreateSession(User user, AfferenceRing ring)
    {
        if (user == null) { Debug.LogError("No User!"); return; }
        if (ring == null) { Debug.LogError("No Ring!"); return; }

        hf = new HapticFactory();
        hs = hf.CreateSession(user, ring);

        var product = Products.AfferenceRing;
        lateralBounder = user.GetPulseManager(product, new BodyLocation("D2", "lateral"));
        medialBounder = user.GetPulseManager(product, new BodyLocation("D2", "medial"));

        calibrationEncoderLateral = hs.CreateEncoder(EncoderModel.DirectStim,
            new BodyLocation("D2", "lateral"), new BodyLocation("D2", "dorsal"));
        calibrationEncoderMedial = hs.CreateEncoder(EncoderModel.DirectStim,
            new BodyLocation("D2", "medial"), new BodyLocation("D2", "dorsal"));

        calibrationEncoderLateral.BuildEncoder(EncoderModel.DirectStim);
        calibrationEncoderMedial.BuildEncoder(EncoderModel.DirectStim);

        qualityEncoders ??= new List<IQualityEncoder>();
        qualityEncoders.Clear();
        qualityEncoders.Add(calibrationEncoderLateral);
        qualityEncoders.Add(calibrationEncoderMedial);

        LoadAllEncoders();
    }

    void LoadAllEncoders()
    {
        string directory = Path.Combine(Application.persistentDataPath, "Encoders");
        if (!Directory.Exists(directory))
        {
            Debug.LogWarning($"Encoder directory not found: {directory}");
            return;
        }

        foreach (var file in Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                string json = File.ReadAllText(file);

                // Lateral encoder
                IQualityEncoder lateralEncoder = hs.CreateEncoder(
                    EncoderModel.Haptic,
                    new BodyLocation("D2", "lateral"),
                    new BodyLocation("D2", "dorsal")
                );
                qualityEncoders.Add(lateralEncoder);
                lateralEncoder.LoadEncoder(file, json);
                CacheBurstTrainsFromJson(lateralEncoder.ID, json);

                // Medial encoder
                IQualityEncoder medialEncoder = hs.CreateEncoder(
                    EncoderModel.Haptic,
                    new BodyLocation("D2", "medial"),
                    new BodyLocation("D2", "dorsal")
                );
                qualityEncoders.Add(medialEncoder);
                medialEncoder.LoadEncoder(file, json);
                CacheBurstTrainsFromJson(medialEncoder.ID, json);

                Debug.Log($"Loaded encoders from {Path.GetFileName(file)}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error loading encoder file {file}: {ex}");
            }
        }

        ActivateEncoders(2, 3);
    }

    public void ActivateEncoders(int? lateralEncoder, int? medialEncoder)
    {
        int? lat = (lateralEncoder.HasValue && lateralEncoder.Value >= 0 && lateralEncoder.Value < qualityEncoders.Count)
            ? lateralEncoder.Value : (int?)null;
        int? med = (medialEncoder.HasValue && medialEncoder.Value >= 0 && medialEncoder.Value < qualityEncoders.Count)
            ? medialEncoder.Value : (int?)null;

        if (lat == _activeLatIndex && med == _activeMedIndex) { Debug.Log("Encoders already active"); return; }

        activeEncoders.Clear();
        if (stimActive) hs.StopEngine();

        for (int i = 0; i < qualityEncoders.Count; i++)
        {
            bool shouldBeActive = (lat.HasValue && i == lat.Value) || (med.HasValue && i == med.Value);
            if (shouldBeActive)
            {
                qualityEncoders[i].Activate();
                hs.ActivateEncoder(qualityEncoders[i].ID);
                activeEncoders.Add(qualityEncoders[i]);
            }
            else
            {
                qualityEncoders[i].Deactivate();
                hs.DeActivateEncoder(qualityEncoders[i].ID);
            }
        }

        if (stimActive) hs.StartEngine();
        _activeLatIndex = lat;
        _activeMedIndex = med;
    }

    public void SendHaptic(float hapticValue)
    {
        if (!stimActive) return;
        foreach (var encoder in activeEncoders)
            encoder.AddPointandUpdate((double)hapticValue, DateTime.Now);
    }


    public void SetUserVolume(float dir)
    {
        if (dir > 0)
        {
            userVolume += volChangeStep;
        }
        else
        {
            userVolume -= volChangeStep;
        }
        userVolume = Mathf.Clamp(userVolume, -5, 3);
        currentUser.UserVolume = userVolume;
    }

    public void ToggleStim() { if (stimActive) StopStim(); else StartStim(); }

    void StartStim()
    {
        hs.StartEngine();
        stimActive = true;
    }

    void StopStim()
    {
        hs.StopEngine();
        stimActive = false;
    }

    private void SafeShutdown()
    {
        if (isShuttingDown) return;
        isShuttingDown = true;

        try { _cts?.Cancel(); _cts?.Dispose(); _cts = null; } catch { }

        try { if (stimActive) stimActive = false; hs?.StopEngine(); } catch (Exception e) { Debug.LogWarning(e); }

        try { if (hs != null) { hf?.RemoveSession(hs.ID); hs = null; } } catch (Exception e) { Debug.LogWarning(e); }

        try { transport?.Dispose(); transport = null; } catch (Exception e) { Debug.LogWarning(e); }

#if UNITY_ANDROID && !UNITY_EDITOR
        AfferenceAndroidBridge.HardCloseGatt();
#endif
    }

    private void CacheBurstTrainsFromJson(Guid encoderId, string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("Nodes", out var nodes)) return;

            var list = new List<string>();
            foreach (var n in nodes.EnumerateArray())
            {
                string type = n.TryGetProperty("Type", out var tEl) ? tEl.GetString() ?? "" : "";
                string subType = n.TryGetProperty("SubType", out var sEl) ? sEl.GetString() ?? "" : "";
                string name = n.TryGetProperty("Name", out var nmEl) ? nmEl.GetString() ?? "" : "";

                bool isBurstTrain = string.Equals(type, "Train", StringComparison.OrdinalIgnoreCase) &&
                                    (subType.IndexOf("Burst", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     subType.IndexOf("AfferenceRingBurstTrain", StringComparison.OrdinalIgnoreCase) >= 0);

                if (isBurstTrain && !string.IsNullOrWhiteSpace(name))
                    list.Add(name);
            }

            if (list.Count > 0)
                _burstTrainNames[encoderId] = list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[HapticManager] CacheBurstTrainsFromJson failed: {ex.Message}");
        }
    }

    private void OnDisable() { SafeShutdown(); }
    private void OnDestroy() { SafeShutdown(); }
    private void OnApplicationQuit() { SafeShutdown(); }
}
