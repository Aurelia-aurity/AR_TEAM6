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
        }
    }

    private void OnEnable()
    {
        GameEventBus.OnRunSessionRequested += OnRunSessionRequested;
        GameEventBus.OnCameraInputStateChanged += OnCameraInputStateChanged;
    }

    private void OnDisable()
    {
        GameEventBus.OnRunSessionRequested -= OnRunSessionRequested;
        GameEventBus.OnCameraInputStateChanged -= OnCameraInputStateChanged;
        hasPendingRequest = false;
        waitingStreamRequestId = 0;

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

    private void OnCameraInputStateChanged(CameraInputStateData state)
    {
        if (!isActiveAndEnabled || !string.Equals(state.CameraId, TargetCameraId, StringComparison.Ordinal))
        {
            return;
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

        if (GameEventBus.IsSessionRunning
            && (state.State == CameraInputState.Error || state.State == CameraInputState.Stopped))
        {
            GameEventBus.PublishSessionRunningChanged(false);
        }
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
