using System;
using UnityEngine;

/// <summary>
/// Bridges shared play-session requests to GpuMalletTracker lifecycle on the main thread.
/// </summary>
[DisallowMultipleComponent]
public sealed class TrackingSessionController : MonoBehaviour
{
    [SerializeField] private GpuMalletTracker tracker;

    public Func<string, (bool available, string error)> CameraAvailabilityProbe { get; set; }

    private bool hasPendingRequest;
    private RunSessionRequest pendingRequest;
    private int waitingStreamRequestId;
    private bool hasCameraState;
    private CameraInputStateData currentCameraState;

    private string TargetCameraId =>
        tracker != null && !string.IsNullOrEmpty(tracker.cameraId) ? tracker.cameraId : "primary";

    private void Awake()
    {
        if (tracker == null)
        {
            tracker = GetComponent<GpuMalletTracker>();
        }

        if (tracker != null)
        {
            tracker.enabled = false;
            tracker.EnsureDefaultCalibrationCaptured();
        }
    }

    private void OnEnable()
    {
        hasCameraState = false;
        currentCameraState = default;
        if (tracker != null && !string.IsNullOrEmpty(GameEventBus.SelectedCameraDeviceName))
        {
            tracker.cameraDeviceName = GameEventBus.SelectedCameraDeviceName;
        }

        GameEventBus.OnRunSessionRequested += OnRunSessionRequested;
        GameEventBus.OnCameraInputStateChanged += OnCameraInputStateChanged;
        GameEventBus.OnCameraDeviceSelected += OnCameraDeviceSelected;
        GameEventBus.OnCalibrationCompleted += OnCalibrationCompleted;
        GameEventBus.OnCalibrationReset += OnCalibrationReset;
        GameEventBus.OnCameraFrameReceived += OnCameraFrameReceived;
    }

    private void OnDisable()
    {
        GameEventBus.OnRunSessionRequested -= OnRunSessionRequested;
        GameEventBus.OnCameraInputStateChanged -= OnCameraInputStateChanged;
        GameEventBus.OnCameraDeviceSelected -= OnCameraDeviceSelected;
        GameEventBus.OnCalibrationCompleted -= OnCalibrationCompleted;
        GameEventBus.OnCalibrationReset -= OnCalibrationReset;
        GameEventBus.OnCameraFrameReceived -= OnCameraFrameReceived;
        hasPendingRequest = false;
        waitingStreamRequestId = 0;
        hasCameraState = false;
        currentCameraState = default;

        if (tracker != null && tracker.enabled)
        {
            tracker.enabled = false;
        }

        if (GameEventBus.IsSessionRunning)
        {
            GameEventBus.PublishSessionRunningChanged(false);
        }
    }

    private void OnRunSessionRequested(RunSessionRequest request)
    {
        hasPendingRequest = true;
        pendingRequest = request;
    }

    private void OnCameraDeviceSelected(string deviceName)
    {
        if (tracker == null)
        {
            return;
        }

        string nextDevice = deviceName ?? string.Empty;
        if (string.Equals(tracker.cameraDeviceName, nextDevice, StringComparison.Ordinal))
        {
            return;
        }

        tracker.cameraDeviceName = nextDevice;
        tracker.PrepareCalibrationForDevice(nextDevice, Guid.Empty);
        if (tracker.enabled)
        {
            var probe = CameraAvailabilityProbe ?? DefaultCameraProbe;
            (bool available, string probeError) = probe(nextDevice);
            if (!available)
            {
                tracker.enabled = false;
                PublishCameraUnavailable(probeError);
                if (GameEventBus.IsSessionRunning)
                {
                    GameEventBus.PublishSessionRunningChanged(false);
                }
                return;
            }

            tracker.enabled = false;
            tracker.enabled = true;
        }
    }

    private void Update()
    {
        if (!hasPendingRequest)
        {
            return;
        }

        RunSessionRequest request = pendingRequest;
        hasPendingRequest = false;

        if (!request.Run)
        {
            waitingStreamRequestId = 0;
            if (tracker != null && tracker.enabled)
            {
                tracker.enabled = false;
            }

            GameEventBus.PublishSessionRunningChanged(false);
            GameEventBus.PublishRunSessionCompleted(new RunSessionResult(request.RequestId, true));
            return;
        }

        if (tracker == null || tracker.trackerShader == null)
        {
            const string error = "트래커 또는 Compute Shader가 설정되지 않았습니다.";
            GameEventBus.PublishSessionRunningChanged(false);
            GameEventBus.PublishRunSessionCompleted(new RunSessionResult(request.RequestId, false, error));
            return;
        }

        var probe = CameraAvailabilityProbe ?? DefaultCameraProbe;
        (bool available, string probeError) = probe(tracker.cameraDeviceName);
        if (!available)
        {
            PublishCameraUnavailable(probeError);
            GameEventBus.PublishSessionRunningChanged(false);
            GameEventBus.PublishRunSessionCompleted(new RunSessionResult(request.RequestId, false, probeError));
            return;
        }

        waitingStreamRequestId = request.RequestId;
        if (tracker.enabled)
        {
            tracker.enabled = false;
        }

        tracker.enabled = true;
    }

    private void OnCalibrationCompleted(
        Vector2 calibratedPixelMin,
        Vector2 calibratedPixelMax,
        double calibratedAreaMin,
        double calibratedAreaMax)
    {
        if (tracker != null && !tracker.enabled)
        {
            tracker.ApplyCalibration(
                calibratedPixelMin,
                calibratedPixelMax,
                calibratedAreaMin,
                calibratedAreaMax);
        }
    }

    private void OnCalibrationReset()
    {
        if (tracker != null && !tracker.enabled)
        {
            tracker.ResetCalibrationToDefaults();
        }
    }

    private void OnCameraFrameReceived(CameraFrameData frame)
    {
        if (!isActiveAndEnabled
            || tracker == null
            || tracker.enabled
            || !hasCameraState
            || currentCameraState.State != CameraInputState.Streaming
            || !string.Equals(frame.CameraId, TargetCameraId, StringComparison.Ordinal)
            || frame.SessionId != currentCameraState.SessionId
            || frame.Texture == null
            || tracker.IsCameraCalibrationReady)
        {
            return;
        }

        tracker.InitializeCameraCalibrationForFrame(
            frame.Texture.width,
            frame.Texture.height,
            false,
            currentCameraState.DeviceName,
            frame.SessionId);
    }

    private void OnCameraInputStateChanged(CameraInputStateData state)
    {
        if (!TryAcceptCameraState(state, out bool isNewSession))
        {
            return;
        }

        if (tracker != null && !tracker.enabled)
        {
            if (isNewSession
                || state.State == CameraInputState.Stopped
                || state.State == CameraInputState.Error)
            {
                tracker.InvalidateFrameCalibration();
            }

            if (state.State == CameraInputState.Waiting || state.State == CameraInputState.Streaming)
            {
                tracker.PrepareCalibrationForDevice(state.DeviceName, state.SessionId);
            }
        }

        if (waitingStreamRequestId != 0)
        {
            if (state.State == CameraInputState.Streaming)
            {
                int id = waitingStreamRequestId;
                waitingStreamRequestId = 0;
                GameEventBus.PublishSessionRunningChanged(true);
                GameEventBus.PublishRunSessionCompleted(new RunSessionResult(id, true));
            }
            else if (state.State == CameraInputState.Error)
            {
                int id = waitingStreamRequestId;
                waitingStreamRequestId = 0;
                GameEventBus.PublishSessionRunningChanged(false);
                GameEventBus.PublishRunSessionCompleted(
                    new RunSessionResult(id, false, state.Description ?? "카메라 입력을 시작할 수 없습니다."));
            }

            return;
        }

        if (state.State == CameraInputState.Streaming)
        {
            if (!GameEventBus.IsSessionRunning)
            {
                GameEventBus.PublishSessionRunningChanged(true);
            }
        }
        else if (GameEventBus.IsSessionRunning
            && (state.State == CameraInputState.Error || state.State == CameraInputState.Stopped))
        {
            GameEventBus.PublishSessionRunningChanged(false);
        }
    }

    private bool TryAcceptCameraState(CameraInputStateData state, out bool isNewSession)
    {
        isNewSession = false;
        if (!isActiveAndEnabled
            || !string.Equals(state.CameraId, TargetCameraId, StringComparison.Ordinal)
            || state.State < CameraInputState.Waiting
            || state.State > CameraInputState.Stopped
            || double.IsNaN(state.ChangedAtMs)
            || double.IsInfinity(state.ChangedAtMs)
            || state.ChangedAtMs < 0.0)
        {
            return false;
        }

        isNewSession = !hasCameraState || state.SessionId != currentCameraState.SessionId;
        if (hasCameraState
            && state.SessionId != Guid.Empty
            && currentCameraState.SessionId != Guid.Empty)
        {
            if (state.ChangedAtMs < currentCameraState.ChangedAtMs)
            {
                return false;
            }

            if (isNewSession && state.State != CameraInputState.Waiting)
            {
                return false;
            }

            if (!isNewSession)
            {
                if (state.State == CameraInputState.Waiting
                    && currentCameraState.State != CameraInputState.Waiting)
                {
                    return false;
                }

                if ((currentCameraState.State == CameraInputState.Error
                        || currentCameraState.State == CameraInputState.Stopped)
                    && state.State == CameraInputState.Streaming)
                {
                    return false;
                }
            }
        }

        currentCameraState = state;
        hasCameraState = true;
        return true;
    }

    private void PublishCameraUnavailable(string error)
    {
        Guid sessionId = Guid.NewGuid();
        double nowMs = Time.realtimeSinceStartupAsDouble * 1000.0;
        GameEventBus.PublishCameraInputState(new CameraInputStateData
        {
            CameraId = TargetCameraId,
            SessionId = sessionId,
            DeviceName = tracker != null ? tracker.cameraDeviceName : string.Empty,
            State = CameraInputState.Waiting,
            ChangedAtMs = nowMs,
            Error = CameraInputError.None
        });
        GameEventBus.PublishCameraInputState(new CameraInputStateData
        {
            CameraId = TargetCameraId,
            SessionId = sessionId,
            DeviceName = tracker != null ? tracker.cameraDeviceName : string.Empty,
            State = CameraInputState.Error,
            ChangedAtMs = nowMs,
            Error = CameraInputError.DeviceNotFound,
            Description = error
        });
    }

    private static (bool available, string error) DefaultCameraProbe(string deviceName)
    {
        WebCamDevice[] devices = WebCamTexture.devices;
        if (devices == null || devices.Length == 0)
        {
            return (false, "사용 가능한 카메라 장치가 없습니다.");
        }

        if (!string.IsNullOrEmpty(deviceName))
        {
            for (int i = 0; i < devices.Length; i++)
            {
                if (string.Equals(devices[i].name, deviceName, StringComparison.Ordinal))
                {
                    return (true, null);
                }
            }

            return (false, $"요청한 카메라 '{deviceName}'을(를) 찾을 수 없습니다.");
        }

        return (true, null);
    }
}
