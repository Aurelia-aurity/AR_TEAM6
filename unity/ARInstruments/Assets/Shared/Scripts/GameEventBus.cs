using System;
using System.Collections.Generic;
using UnityEngine;

// Contract: docs/event_bus_architecture_data_structures_ko.md.
// Publish on the Unity main thread; unsubscribe when components are disabled.
public static class GameEventBus
{
    // Zero supports existing stand-alone previews. B begins a positive version before applying it.
    public static long ContextVersion { get; private set; }
    public static bool IsContextActive { get; private set; } = true;

    public static void BeginContext(long version)
    {
        if (version <= ContextVersion)
            throw new ArgumentOutOfRangeException(nameof(version));
        ContextVersion = version;
        IsContextActive = false;
    }

    public static void ActivateContext(long version)
    {
        if (version == ContextVersion && version > 0)
            IsContextActive = true;
    }

    public static bool IsActiveContext(long version) => IsContextActive && version == ContextVersion;

    public static bool IsSessionRunning { get; private set; }
    public static InstrumentType SelectedInstrument { get; private set; } = InstrumentType.Janggu;
    public static string SelectedCameraDeviceName { get; private set; } = string.Empty;

    public static event Action<RunSessionRequest> OnRunSessionRequested;
    public static event Action<RunSessionResult> OnRunSessionCompleted;
    public static event Action<bool> OnSessionRunningChanged;
    public static event Action<InstrumentType> OnInstrumentSelected;
    public static event Action<string> OnCameraDeviceSelected;

    public static void PublishRunSessionRequest(RunSessionRequest request)
    {
        OnRunSessionRequested?.Invoke(request);
    }

    public static void PublishRunSessionCompleted(RunSessionResult result)
    {
        OnRunSessionCompleted?.Invoke(result);
    }

    public static void PublishSessionRunningChanged(bool isRunning)
    {
        IsSessionRunning = isRunning;
        OnSessionRunningChanged?.Invoke(isRunning);
    }

    public static void PublishInstrumentSelected(InstrumentType instrument)
    {
        if (!Enum.IsDefined(typeof(InstrumentType), instrument))
        {
            throw new ArgumentOutOfRangeException(nameof(instrument));
        }

        SelectedInstrument = instrument;
        OnInstrumentSelected?.Invoke(instrument);
    }

    public static void PublishCameraDeviceSelected(string deviceName)
    {
        SelectedCameraDeviceName = deviceName ?? string.Empty;
        OnCameraDeviceSelected?.Invoke(SelectedCameraDeviceName);
    }

    // Camera publishers must use the publish methods so cached state stays in sync.
    public static event Action<CameraFrameData> OnCameraFrameReceived;
    public static event Action<CameraInputStateData> OnCameraInputStateChanged;

    private static readonly Dictionary<string, CameraInputStateData> cameraStates =
        new Dictionary<string, CameraInputStateData>(StringComparer.Ordinal);

    // Synchronous notification only: never cache or take ownership of a texture.
    public static void PublishCameraFrame(CameraFrameData frame)
    {
        OnCameraFrameReceived?.Invoke(frame);
    }

    public static void PublishCameraInputState(CameraInputStateData state)
    {
        if (string.IsNullOrEmpty(state.CameraId))
        {
            return;
        }

        cameraStates[state.CameraId] = state;
        OnCameraInputStateChanged?.Invoke(state);
    }

    // Subscribe first, then query on the main thread. Streaming still requires
    // waiting for a new frame; the cached state contains no texture.
    public static bool TryGetCameraInputState(string cameraId, out CameraInputStateData state)
    {
        if (string.IsNullOrEmpty(cameraId))
        {
            state = default;
            return false;
        }

        return cameraStates.TryGetValue(cameraId, out state);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCameraInput()
    {
        ContextVersion = 0;
        IsContextActive = true;
        IsSessionRunning = false;
        SelectedInstrument = InstrumentType.Janggu;
        SelectedCameraDeviceName = string.Empty;
        cameraStates.Clear();
        OnRunSessionRequested = null;
        OnRunSessionCompleted = null;
        OnSessionRunningChanged = null;
        OnInstrumentSelected = null;
        OnCameraDeviceSelected = null;
        OnCameraFrameReceived = null;
        OnCameraInputStateChanged = null;
        OnMalletsTracked = null;
        OnTrackingLost = null;
        OnTrackingLostAt = null;
        OnRawMalletSample = null;
        OnCalibrationCompleted = null;
        OnValidHit = null;
    }

    // A -> B, C: left and right mallet states from one analyzed frame.
    public static Action<MalletState, MalletState> OnMalletsTracked;

    // Legacy notification without ordering information. Kept for existing subscribers.
    public static Action<int> OnTrackingLost;

    // A -> B: mallet ID and the failed observation's TimestampMs, using the same
    // clock as MalletState. Ordered consumers should use this instead of OnTrackingLost.
    public static Action<int, double> OnTrackingLostAt;

    public static void PublishTrackingLost(int malletId, double timestampMs)
    {
        OnTrackingLostAt?.Invoke(malletId, timestampMs);
        OnTrackingLost?.Invoke(malletId);
    }

    // A -> calibration: raw values required to build a CoordinateMapper calibration.
    public static Action<RawMalletSample> OnRawMalletSample;

    // B -> A: calibrated screen pixel bounds and area bounds.
    public static Action<Vector2, Vector2, double, double> OnCalibrationCompleted;

    // C -> presentation and audio: one valid hit.
    public static Action<HitEvent> OnValidHit;
}
