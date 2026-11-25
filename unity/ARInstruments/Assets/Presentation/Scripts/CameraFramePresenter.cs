using System;
using UnityEngine;

/// <summary>Connects A's camera events to B's view without owning camera resources.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CameraDisplayView))]
[DefaultExecutionOrder(100)]
public sealed class CameraFramePresenter : MonoBehaviour
{
    [SerializeField, Tooltip("Logical camera ID from A, not the device name. Applied on enable.")]
    private string cameraId = "primary";

    private CameraDisplayView display;
    private string activeCameraId;
    private bool hasState;
    private CameraInputStateData currentState;
    private double lastFrameAtMs;
    private long lastFrameSequence;

    private void OnEnable()
    {
        display = GetComponent<CameraDisplayView>();
        activeCameraId = cameraId;
        hasState = false;
        lastFrameAtMs = double.NegativeInfinity;
        display.ShowWaiting();
        if (string.IsNullOrWhiteSpace(activeCameraId))
        {
            Debug.LogError("CameraFramePresenter requires a logical Camera Id.", this);
            enabled = false;
            return;
        }

        GameEventBus.OnCameraInputStateChanged += OnInputStateChanged;
        GameEventBus.OnCameraFrameReceived += OnFrameReceived;
        if (GameEventBus.TryGetCameraInputState(activeCameraId, out var state))
            OnInputStateChanged(state);
    }

    private void OnDisable()
    {
        GameEventBus.OnCameraInputStateChanged -= OnInputStateChanged;
        GameEventBus.OnCameraFrameReceived -= OnFrameReceived;
        hasState = false;
        if (display != null)
            display.ShowWaiting();
    }

    private void OnInputStateChanged(CameraInputStateData state)
    {
        if (!isActiveAndEnabled || state.CameraId != activeCameraId
            || state.SessionId == Guid.Empty || !IsValidTime(state.ChangedAtMs)
            || state.State < CameraInputState.Waiting || state.State > CameraInputState.Stopped)
            return;

        bool newSession = !hasState || state.SessionId != currentState.SessionId;
        if (hasState)
        {
            if (state.ChangedAtMs < currentState.ChangedAtMs || state.ChangedAtMs < lastFrameAtMs)
                return;
            // Once subscribed, a different session must announce a new start.
            // The first/cached state may be Error, Stopped or Streaming.
            if (newSession && state.State != CameraInputState.Waiting)
                return;
            if (!newSession)
            {
                if (state.State == CameraInputState.Waiting && currentState.State != CameraInputState.Waiting)
                    return;
                if ((currentState.State == CameraInputState.Error || currentState.State == CameraInputState.Stopped)
                    && state.State == CameraInputState.Streaming)
                    return;
            }
        }

        bool wasStreaming = hasState && currentState.State == CameraInputState.Streaming;
        currentState = state;
        hasState = true;
        if (newSession)
        {
            lastFrameAtMs = double.NegativeInfinity;
            lastFrameSequence = 0;
        }

        switch (state.State)
        {
            case CameraInputState.Streaming:
                // A state event never restores an old texture. Wait for a frame.
                if (newSession || !wasStreaming)
                    display.ShowWaiting("Waiting for a new camera frame.");
                break;
            case CameraInputState.Error:
                display.ShowError(ErrorDescription(state));
                break;
            case CameraInputState.Stopped:
                display.ShowWaiting("Camera input stopped.");
                break;
            default:
                display.ShowWaiting("Waiting for camera input.");
                break;
        }
    }

    private void OnFrameReceived(CameraFrameData frame)
    {
        if (!isActiveAndEnabled || !hasState || currentState.State != CameraInputState.Streaming
            || frame.CameraId != activeCameraId || frame.SessionId != currentState.SessionId
            || !IsValidTime(frame.ReceivedAtMs) || frame.ReceivedAtMs < currentState.ChangedAtMs
            || frame.FrameSequence < 0
            || (lastFrameSequence > 0 && frame.FrameSequence <= lastFrameSequence)
            || frame.ReceivedAtMs < lastFrameAtMs
            || (frame.FrameSequence == 0 && frame.ReceivedAtMs == lastFrameAtMs))
            return;

        lastFrameSequence = frame.FrameSequence;
        lastFrameAtMs = frame.ReceivedAtMs;
        display.PresentFrame(frame.Texture, frame.ClockwiseRotation, frame.VerticallyMirrored,
            string.IsNullOrEmpty(currentState.DeviceName) ? "Camera input active." : currentState.DeviceName);
    }

    private static bool IsValidTime(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;

    private static string ErrorDescription(CameraInputStateData state)
    {
        if (!string.IsNullOrEmpty(state.Description))
            return state.Description;
        switch (state.Error)
        {
            case CameraInputError.DeviceNotFound:
                return "The selected camera was not found. Check the camera connection.";
            case CameraInputError.StartFailed:
                return "The camera could not start. Check camera access and try again.";
            case CameraInputError.FrameTimeout:
                return "Camera frames stopped arriving. Check the connection and restart the input.";
            default:
                return "The camera input is unavailable.";
        }
    }
}
