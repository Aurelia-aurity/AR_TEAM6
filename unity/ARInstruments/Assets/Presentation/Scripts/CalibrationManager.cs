using System;
using UnityEngine;

public enum CalibrationStep : byte
{
    NotStarted = 0,
    TopLeft = 1,
    TopRight = 2,
    BottomLeft = 3,
    BottomRight = 4,
    RestOnDesk = 5,
    Completed = 6
}

/// <summary>
/// Fixed-state calibration controller. It samples raw camera measurements without
/// coroutines or per-frame allocations.
/// </summary>
public sealed class CalibrationManager : MonoBehaviour
{
    public const int SamplesPerStep = 60;
    private const string PrimaryCameraId = "primary";

    public CalibrationStep CurrentStep { get; private set; }
    public int CurrentSampleCount => currentSampleIndex;
    public bool IsCalibrating => CurrentStep > CalibrationStep.NotStarted && CurrentStep < CalibrationStep.Completed;

    public event Action<CalibrationStep, int, int> ProgressChanged;

    private int currentSampleIndex;
    private double sumX;
    private double sumY;
    private double sumArea;
    private Vector2 minPixel;
    private Vector2 maxPixel;
    private double minArea;
    private double maxArea;
    private Vector2 topLeft;
    private Vector2 topRight;
    private Vector2 bottomLeft;
    private Vector2 bottomRight;
    private double topLeftArea;
    private double topRightArea;
    private double bottomLeftArea;
    private double bottomRightArea;
    private bool hasCameraState;
    private CameraInputStateData currentCameraState;
    private string activeCameraDeviceName = string.Empty;
    private Guid activeCameraSessionId;
    private string calibrationDeviceName = string.Empty;
    private Guid calibrationSessionId;

    private void OnEnable()
    {
        hasCameraState = false;
        currentCameraState = default;
        activeCameraSessionId = Guid.Empty;
        activeCameraDeviceName = GameEventBus.SelectedCameraDeviceName ?? string.Empty;
        if (GameEventBus.TryGetCameraInputState(PrimaryCameraId, out CameraInputStateData cached)
            && cached.SessionId != Guid.Empty)
        {
            currentCameraState = cached;
            hasCameraState = true;
            activeCameraSessionId = cached.SessionId;
            if (!string.IsNullOrEmpty(cached.DeviceName))
            {
                activeCameraDeviceName = cached.DeviceName;
            }
        }

        GameEventBus.OnRawMalletSample += ProcessCalibrationSample;
        GameEventBus.OnCameraDeviceSelected += OnCameraDeviceSelected;
        GameEventBus.OnCameraInputStateChanged += OnCameraInputStateChanged;
    }

    private void OnDisable()
    {
        GameEventBus.OnRawMalletSample -= ProcessCalibrationSample;
        GameEventBus.OnCameraDeviceSelected -= OnCameraDeviceSelected;
        GameEventBus.OnCameraInputStateChanged -= OnCameraInputStateChanged;
        hasCameraState = false;
    }

    public void Begin()
    {
        ResetCalibrationData();
        calibrationDeviceName = activeCameraDeviceName;
        calibrationSessionId = activeCameraSessionId;
        CurrentStep = CalibrationStep.NotStarted;
        StartNextStep();
    }

    public void Cancel()
    {
        CurrentStep = CalibrationStep.NotStarted;
        ResetCalibrationData();
        calibrationDeviceName = string.Empty;
        calibrationSessionId = Guid.Empty;
        ProgressChanged?.Invoke(CurrentStep, currentSampleIndex, SamplesPerStep);
    }

    public void StartNextStep()
    {
        if (CurrentStep >= CalibrationStep.Completed)
        {
            return;
        }

        CurrentStep++;
        currentSampleIndex = 0;
        sumX = 0.0;
        sumY = 0.0;
        sumArea = 0.0;
        ProgressChanged?.Invoke(CurrentStep, currentSampleIndex, SamplesPerStep);
    }

    private void ProcessCalibrationSample(RawMalletSample sample)
    {
        if (CurrentStep == CalibrationStep.NotStarted
            || CurrentStep == CalibrationStep.Completed
            || sample.MalletId != 0)
        {
            return;
        }

        sumX += sample.PixelPosition.x;
        sumY += sample.PixelPosition.y;
        sumArea += sample.PixelArea;
        currentSampleIndex++;
        if (currentSampleIndex >= SamplesPerStep)
        {
            RecordCurrentStep();
            StartNextStep();
        }
        else
        {
            ProgressChanged?.Invoke(CurrentStep, currentSampleIndex, SamplesPerStep);
        }
    }

    private void RecordCurrentStep()
    {
        Vector2 averagePixel = new Vector2(
            (float)(sumX / SamplesPerStep),
            (float)(sumY / SamplesPerStep));
        double averageArea = sumArea / SamplesPerStep;

        switch (CurrentStep)
        {
            case CalibrationStep.TopLeft:
                topLeft = averagePixel;
                topLeftArea = averageArea;
                break;
            case CalibrationStep.TopRight:
                topRight = averagePixel;
                topRightArea = averageArea;
                break;
            case CalibrationStep.BottomLeft:
                bottomLeft = averagePixel;
                bottomLeftArea = averageArea;
                break;
            case CalibrationStep.BottomRight:
                bottomRight = averagePixel;
                bottomRightArea = averageArea;
                break;
            case CalibrationStep.RestOnDesk:
                minArea = averageArea;
                break;
        }

        if (CurrentStep == CalibrationStep.BottomRight)
        {
            minPixel = new Vector2(
                Mathf.Min(topLeft.x, bottomLeft.x),
                Mathf.Min(topLeft.y, topRight.y));
            maxPixel = new Vector2(
                Mathf.Max(topRight.x, bottomRight.x),
                Mathf.Max(bottomLeft.y, bottomRight.y));
            maxArea = Mathf.Max(
                (float)topLeftArea,
                Mathf.Max(
                    (float)topRightArea,
                    Mathf.Max(
                        (float)bottomLeftArea,
                        (float)bottomRightArea)));
        }

        if (CurrentStep == CalibrationStep.RestOnDesk)
        {
            maxArea = System.Math.Max(maxArea, minArea + 1.0);
            GameEventBus.OnCalibrationCompleted?.Invoke(
                minPixel,
                maxPixel,
                minArea,
                maxArea);
        }
    }

    private void OnCameraDeviceSelected(string deviceName)
    {
        string nextDevice = deviceName ?? string.Empty;
        bool changed = !string.Equals(activeCameraDeviceName, nextDevice, StringComparison.Ordinal);
        activeCameraDeviceName = nextDevice;
        if (changed && CurrentStep != CalibrationStep.NotStarted)
        {
            InvalidateCalibrationForDeviceChange();
        }
    }

    private void InvalidateCalibrationForDeviceChange()
    {
        Cancel();
        GameEventBus.PublishCalibrationReset();
    }

    private void OnCameraInputStateChanged(CameraInputStateData state)
    {
        if (!isActiveAndEnabled
            || !string.Equals(state.CameraId, PrimaryCameraId, StringComparison.Ordinal)
            || state.State < CameraInputState.Waiting
            || state.State > CameraInputState.Stopped
            || double.IsNaN(state.ChangedAtMs)
            || double.IsInfinity(state.ChangedAtMs)
            || state.ChangedAtMs < 0.0)
        {
            return;
        }

        if (hasCameraState
            && state.SessionId != Guid.Empty
            && currentCameraState.SessionId != Guid.Empty)
        {
            if (state.ChangedAtMs < currentCameraState.ChangedAtMs)
            {
                return;
            }

            bool newSession = state.SessionId != currentCameraState.SessionId;
            if (newSession && state.State != CameraInputState.Waiting)
            {
                return;
            }

            if (!newSession)
            {
                if (state.State == CameraInputState.Waiting
                    && currentCameraState.State != CameraInputState.Waiting)
                {
                    return;
                }

                if ((currentCameraState.State == CameraInputState.Error
                        || currentCameraState.State == CameraInputState.Stopped)
                    && state.State == CameraInputState.Streaming)
                {
                    return;
                }
            }
        }

        currentCameraState = state;
        hasCameraState = true;
        if (state.SessionId != Guid.Empty)
        {
            activeCameraSessionId = state.SessionId;
        }

        string incomingDevice = state.DeviceName ?? string.Empty;
        if (!string.IsNullOrEmpty(incomingDevice))
        {
            activeCameraDeviceName = incomingDevice;
        }

        if (CurrentStep == CalibrationStep.NotStarted)
        {
            return;
        }

        bool deviceChanged = false;
        if (!string.IsNullOrEmpty(incomingDevice))
        {
            if (!string.IsNullOrEmpty(calibrationDeviceName))
            {
                deviceChanged = !string.Equals(calibrationDeviceName, incomingDevice, StringComparison.Ordinal);
            }
            else if (calibrationSessionId != Guid.Empty
                && state.SessionId != Guid.Empty
                && state.SessionId != calibrationSessionId)
            {
                deviceChanged = true;
            }
        }
        else if (calibrationSessionId != Guid.Empty
            && state.SessionId != Guid.Empty
            && state.SessionId != calibrationSessionId)
        {
            deviceChanged = true;
        }

        if (deviceChanged)
        {
            InvalidateCalibrationForDeviceChange();
            return;
        }

        if (string.IsNullOrEmpty(calibrationDeviceName) && !string.IsNullOrEmpty(incomingDevice))
        {
            calibrationDeviceName = incomingDevice;
        }

        if (calibrationSessionId == Guid.Empty && state.SessionId != Guid.Empty)
        {
            calibrationSessionId = state.SessionId;
        }
    }

    private void ResetCalibrationData()
    {
        currentSampleIndex = 0;
        sumX = 0.0;
        sumY = 0.0;
        sumArea = 0.0;
        minPixel = Vector2.zero;
        maxPixel = Vector2.zero;
        minArea = 0.0;
        maxArea = 0.0;
        topLeft = Vector2.zero;
        topRight = Vector2.zero;
        bottomLeft = Vector2.zero;
        bottomRight = Vector2.zero;
        topLeftArea = 0.0;
        topRightArea = 0.0;
        bottomLeftArea = 0.0;
        bottomRightArea = 0.0;
    }
}
