using System;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Native WebCamTexture and compute-shader tracker for both mallets.
/// Publishes calibrated world-space states through the shared event bus.
/// </summary>
public sealed class GpuMalletTracker : MonoBehaviour
{
    public ComputeShader trackerShader;

    [Header("Camera input")]
    [Tooltip("Logical camera ID used by the presentation event consumer.")]
    public string cameraId = "primary";
    [Tooltip("Exact device name from Log Available Camera Devices. Empty keeps automatic selection. Applied on startup.")]
    public string cameraDeviceName = "";

    [Header("Left Mallet (HSV: 0.0 ~ 1.0)")]
    public Vector3 leftLowerHSV = new Vector3(0.8f, 0.4f, 0.4f);
    public Vector3 leftUpperHSV = new Vector3(0.95f, 1.0f, 1.0f);

    [Header("Right Mallet (HSV: 0.0 ~ 1.0)")]
    public Vector3 rightLowerHSV = new Vector3(0.25f, 0.4f, 0.4f);
    public Vector3 rightUpperHSV = new Vector3(0.4f, 1.0f, 1.0f);

    [Header("Tracking")]
    [Min(0)]
    public uint minPixelArea = 100;
    [Range(0.0f, 1.0f)]
    public float processNoise = 0.05f;
    [Min(0.0001f)]
    public float measurementNoise = 0.5f;

    [Header("World calibration")]
    public Vector2 pixelMin = new Vector2(200.0f, 200.0f);
    public Vector2 pixelMax = new Vector2(1080.0f, 520.0f);
    public double areaMin = 100.0;
    public double areaMax = 3000.0;
    public Vector3 worldMin = new Vector3(-5.0f, -3.0f, 0.0f);
    public Vector3 worldMax = new Vector3(5.0f, 3.0f, 2.0f);

    [Header("Watchdog")]
    [SerializeField] private float startTimeoutSeconds = 5.0f;
    [SerializeField] private float frameTimeoutSeconds = 2.0f;

    private const int ResultCount = 6;
    private const int ThreadsPerGroup = 8;
    private const int ReadbackSlotCount = 3;

    private WebCamTexture webCam;
    private readonly ComputeBuffer[] resultBuffers = new ComputeBuffer[ReadbackSlotCount];
    private readonly bool[] readbackPending = new bool[ReadbackSlotCount];
    private readonly bool[] readbackRequestActive = new bool[ReadbackSlotCount];
    private readonly AsyncGPUReadbackRequest[] readbackRequests = new AsyncGPUReadbackRequest[ReadbackSlotCount];
    private readonly double[] dispatchTimestamps = new double[ReadbackSlotCount];
    private readonly uint[] clearData = new uint[ResultCount];
    private readonly long[] dispatchVersions = new long[ReadbackSlotCount];
    private readonly Guid[] dispatchSessions = new Guid[ReadbackSlotCount];
    private long filterVersion;
    private long frameSequence;
    private int nextReadbackSlot;
    private int kernelIndex;
    private readonly KalmanFilter3D leftFilter = new KalmanFilter3D();
    private readonly KalmanFilter3D rightFilter = new KalmanFilter3D();
    private readonly CoordinateMapper coordinateMapper = new CoordinateMapper();
    private Action<AsyncGPUReadbackRequest> onReadbackSlot0;
    private Action<AsyncGPUReadbackRequest> onReadbackSlot1;
    private Action<AsyncGPUReadbackRequest> onReadbackSlot2;
    private bool cameraCalibrationReady;
    private int configuredWidth;
    private int configuredHeight;
    private bool frontFacingCamera;
    private bool cameraStreamingPublished;
    private bool terminalStatePublished;
    private bool shuttingDown;
    private Guid sessionId;
    private double sessionStartMs;
    private double lastFrameReceivedMs;

    private void Awake()
    {
        EnsureReadbackDelegates();
    }

    private void OnEnable()
    {
        EnsureReadbackDelegates();
        GameEventBus.OnCalibrationCompleted += ApplyCalibration;
        shuttingDown = false;
        cameraCalibrationReady = false;
        configuredWidth = 0;
        configuredHeight = 0;
        cameraStreamingPublished = false;
        terminalStatePublished = false;
        frameSequence = 0;
        filterVersion = 0;
        leftFilter.Clear();
        rightFilter.Clear();

        if (string.IsNullOrEmpty(cameraId))
        {
            cameraId = "primary";
        }
        sessionId = Guid.NewGuid();
        PublishCameraInputState(CameraInputState.Waiting, CameraInputError.None);

        if (trackerShader == null)
        {
            Debug.LogError("GpuMalletTracker requires a compute shader.", this);
            PublishCameraInputState(CameraInputState.Error, CameraInputError.StartFailed,
                "GpuMalletTracker requires a compute shader.");
            terminalStatePublished = true;
            enabled = false;
            return;
        }

        if (!SystemInfo.supportsComputeShaders)
        {
            Debug.LogError("This platform does not support compute shaders.", this);
            PublishCameraInputState(CameraInputState.Error, CameraInputError.StartFailed,
                "This platform does not support compute shaders.");
            terminalStatePublished = true;
            enabled = false;
            return;
        }

        if (!SystemInfo.supportsAsyncGPUReadback)
        {
            Debug.LogError("This platform does not support async GPU readback.", this);
            PublishCameraInputState(CameraInputState.Error, CameraInputError.StartFailed,
                "This platform does not support async GPU readback.");
            terminalStatePublished = true;
            enabled = false;
            return;
        }

        WebCamDevice[] devices = WebCamTexture.devices;
        if (devices == null || devices.Length == 0)
        {
            Debug.LogError("No camera device is available for GpuMalletTracker.", this);
            PublishCameraInputState(CameraInputState.Error, CameraInputError.DeviceNotFound,
                "No camera device is available.");
            terminalStatePublished = true;
            enabled = false;
            return;
        }

        LogCameraDevices(devices);
        if (!TrySelectCamera(devices, out WebCamDevice selectedDevice))
        {
            PublishCameraInputState(CameraInputState.Error, CameraInputError.DeviceNotFound,
                "The selected camera was not found.");
            terminalStatePublished = true;
            enabled = false;
            return;
        }

        Debug.Log($"GpuMalletTracker selected camera: '{selectedDevice.name}'.", this);
        frontFacingCamera = selectedDevice.isFrontFacing;
        webCam = new WebCamTexture(selectedDevice.name, 1280, 720, 60);
        webCam.Play();
        sessionStartMs = Time.realtimeSinceStartupAsDouble * 1000.0;
        lastFrameReceivedMs = sessionStartMs;

        for (int i = 0; i < ReadbackSlotCount; i++)
        {
            resultBuffers[i] = new ComputeBuffer(ResultCount, sizeof(uint));
            readbackPending[i] = false;
            readbackRequestActive[i] = false;
            readbackRequests[i] = default;
            dispatchTimestamps[i] = 0.0;
            dispatchVersions[i] = 0;
            dispatchSessions[i] = Guid.Empty;
        }
        nextReadbackSlot = 0;

        kernelIndex = trackerShader.FindKernel("CSMain");
        leftFilter.SetNoiseParameters(processNoise, measurementNoise);
        rightFilter.SetNoiseParameters(processNoise, measurementNoise);
    }

    private void EnsureReadbackDelegates()
    {
        if (onReadbackSlot0 == null)
        {
            onReadbackSlot0 = OnReadbackSlot0;
            onReadbackSlot1 = OnReadbackSlot1;
            onReadbackSlot2 = OnReadbackSlot2;
        }
    }

    [ContextMenu("Log Available Camera Devices")]
    public void LogAvailableCameraDevices()
    {
        LogCameraDevices(WebCamTexture.devices);
    }

    private void LogCameraDevices(WebCamDevice[] devices)
    {
        if (devices == null || devices.Length == 0)
        {
            Debug.LogWarning("No camera devices detected. Connect the phone as a PC camera and check camera permissions.", this);
            return;
        }

        for (int i = 0; i < devices.Length; i++)
        {
            Debug.Log($"Camera [{i}]: '{devices[i].name}' (front facing: {devices[i].isFrontFacing}).", this);
        }
    }

    private bool TrySelectCamera(WebCamDevice[] devices, out WebCamDevice selectedDevice)
    {
        selectedDevice = devices[0];
        if (!string.IsNullOrEmpty(cameraDeviceName))
        {
            for (int i = 0; i < devices.Length; i++)
            {
                if (string.Equals(devices[i].name, cameraDeviceName, StringComparison.Ordinal))
                {
                    selectedDevice = devices[i];
                    return true;
                }
            }

            Debug.LogError($"Requested camera '{cameraDeviceName}' was not found. No other camera will be opened. Use Log Available Camera Devices to check the exact name.", this);
            return false;
        }

        // Preserve the existing automatic selection for scenes without an explicit device.
        for (int i = 0; i < devices.Length; i++)
        {
            if (devices[i].isFrontFacing)
            {
                selectedDevice = devices[i];
                break;
            }
        }

        Debug.LogWarning("Camera Device Name is empty; selecting a camera automatically. Set the exact phone camera name for live input.", this);
        return true;
    }

    private void Update()
    {
        double nowMs = Time.realtimeSinceStartupAsDouble * 1000.0;
        bool hasValidFrame = webCam != null
            && webCam.isPlaying
            && webCam.didUpdateThisFrame
            && webCam.width > 16;

        if (hasValidFrame)
        {
            lastFrameReceivedMs = nowMs;
        }
        else if (!cameraStreamingPublished)
        {
            if ((nowMs - sessionStartMs) > startTimeoutSeconds * 1000.0)
            {
                PublishCameraInputState(CameraInputState.Error, CameraInputError.StartFailed,
                    "Camera start timed out.", nowMs);
                terminalStatePublished = true;
                enabled = false;
            }
            return;
        }
        else
        {
            if (webCam == null || !webCam.isPlaying || (nowMs - lastFrameReceivedMs) > frameTimeoutSeconds * 1000.0)
            {
                PublishCameraInputState(CameraInputState.Error, CameraInputError.FrameTimeout,
                    "Camera frame stream interrupted.", nowMs);
                terminalStatePublished = true;
                enabled = false;
            }
            return;
        }

        int clockwiseRotation = CoordinateMapper.NormalizeRightAngle(webCam.videoRotationAngle);
        bool verticallyMirrored = webCam.videoVerticallyMirrored;
        bool swapsAxes = clockwiseRotation == 90 || clockwiseRotation == 270;
        int orientedWidth = swapsAxes ? webCam.height : webCam.width;
        int orientedHeight = swapsAxes ? webCam.width : webCam.height;

        if (!cameraCalibrationReady || configuredWidth != orientedWidth || configuredHeight != orientedHeight)
        {
            // MalletTracker.compute already applies VerticallyMirrored then ClockwiseRotation.
            coordinateMapper.SetupCamera(
                orientedWidth,
                orientedHeight,
                frontFacingCamera,
                0,
                false);
            coordinateMapper.UpdateCalibration(
                pixelMin,
                pixelMax,
                areaMin,
                areaMax,
                worldMin,
                worldMax);
            configuredWidth = orientedWidth;
            configuredHeight = orientedHeight;
            cameraCalibrationReady = true;
        }

        if (!cameraStreamingPublished)
        {
            PublishCameraInputState(CameraInputState.Streaming, CameraInputError.None, null, nowMs);
            cameraStreamingPublished = true;
        }

        frameSequence++;
        GameEventBus.PublishCameraFrame(new CameraFrameData
        {
            CameraId = cameraId,
            SessionId = sessionId,
            FrameSequence = frameSequence,
            Texture = webCam,
            ClockwiseRotation = webCam.videoRotationAngle,
            VerticallyMirrored = verticallyMirrored,
            ReceivedAtMs = nowMs
        });
        if (!GameEventBus.IsContextActive)
            return;

        int slot = FindAvailableReadbackSlot();
        if (slot < 0)
        {
            return;
        }

        ComputeBuffer resultBuffer = resultBuffers[slot];
        resultBuffer.SetData(clearData);
        trackerShader.SetTexture(kernelIndex, "InputTexture", webCam);
        trackerShader.SetVector("LeftLowerHSV", leftLowerHSV);
        trackerShader.SetVector("LeftUpperHSV", leftUpperHSV);
        trackerShader.SetVector("RightLowerHSV", rightLowerHSV);
        trackerShader.SetVector("RightUpperHSV", rightUpperHSV);
        trackerShader.SetInt("ClockwiseRotation", clockwiseRotation);
        trackerShader.SetInt("VerticallyMirrored", verticallyMirrored ? 1 : 0);
        trackerShader.SetBuffer(kernelIndex, "ResultBuffer", resultBuffer);

        int threadGroupsX = Mathf.CeilToInt(webCam.width / (float)ThreadsPerGroup);
        int threadGroupsY = Mathf.CeilToInt(webCam.height / (float)ThreadsPerGroup);
        trackerShader.Dispatch(kernelIndex, threadGroupsX, threadGroupsY, 1);

        double timestampMs = Time.realtimeSinceStartupAsDouble * 1000.0;
        dispatchVersions[slot] = GameEventBus.ContextVersion;
        dispatchTimestamps[slot] = timestampMs;
        dispatchSessions[slot] = sessionId;
        readbackPending[slot] = true;
        readbackRequestActive[slot] = true;
        switch (slot)
        {
            case 0:
                readbackRequests[slot] = AsyncGPUReadback.Request(resultBuffer, onReadbackSlot0);
                break;
            case 1:
                readbackRequests[slot] = AsyncGPUReadback.Request(resultBuffer, onReadbackSlot1);
                break;
            default:
                readbackRequests[slot] = AsyncGPUReadback.Request(resultBuffer, onReadbackSlot2);
                break;
        }
    }

    private int FindAvailableReadbackSlot()
    {
        for (int i = 0; i < ReadbackSlotCount; i++)
        {
            int slot = (nextReadbackSlot + i) % ReadbackSlotCount;
            if (!readbackPending[slot])
            {
                nextReadbackSlot = (slot + 1) % ReadbackSlotCount;
                return slot;
            }
        }

        return -1;
    }

    private void OnReadbackSlot0(AsyncGPUReadbackRequest request)
    {
        ProcessReadback(0, request);
    }

    private void OnReadbackSlot1(AsyncGPUReadbackRequest request)
    {
        ProcessReadback(1, request);
    }

    private void OnReadbackSlot2(AsyncGPUReadbackRequest request)
    {
        ProcessReadback(2, request);
    }

    private void ProcessReadback(int slot, AsyncGPUReadbackRequest request)
    {
        readbackPending[slot] = false;
        readbackRequestActive[slot] = false;
        readbackRequests[slot] = default;
        if (shuttingDown || !isActiveAndEnabled || dispatchSessions[slot] != sessionId)
        {
            return;
        }

        if (request.hasError)
        {
            Debug.LogError("GpuMalletTracker async readback failed.", this);
            return;
        }

        long version = dispatchVersions[slot];
        if (!GameEventBus.IsActiveContext(version))
            return;
        if (filterVersion != version)
        {
            leftFilter.Clear();
            rightFilter.Clear();
            filterVersion = version;
        }

        var resultData = request.GetData<uint>();
        if (!resultData.IsCreated || resultData.Length < ResultCount)
        {
            return;
        }

        double timestampMs = dispatchTimestamps[slot];
        MalletState leftState = ProcessSample(
            0,
            resultData[0],
            resultData[1],
            resultData[2],
            timestampMs,
            leftFilter);
        MalletState rightState = ProcessSample(
            1,
            resultData[3],
            resultData[4],
            resultData[5],
            timestampMs,
            rightFilter);
        leftState.ContextVersion = rightState.ContextVersion = version;
        GameEventBus.OnMalletsTracked?.Invoke(leftState, rightState);
    }

    internal MalletState ProcessSample(
        int malletId,
        uint sumX,
        uint sumY,
        uint pixelCount,
        double timestampMs,
        KalmanFilter3D filter)
    {
        if (pixelCount <= minPixelArea || !double.IsFinite(timestampMs))
        {
            filter.Clear();
            GameEventBus.PublishTrackingLost(malletId, timestampMs);
            return TrackingLostState(malletId, timestampMs);
        }

        Vector2 centroid = new Vector2(
            sumX / (float)pixelCount,
            sumY / (float)pixelCount);
        if (!float.IsFinite(centroid.x) || !float.IsFinite(centroid.y))
        {
            filter.Clear();
            GameEventBus.PublishTrackingLost(malletId, timestampMs);
            return TrackingLostState(malletId, timestampMs);
        }

        GameEventBus.OnRawMalletSample?.Invoke(new RawMalletSample
        {
            MalletId = malletId,
            PixelPosition = centroid,
            PixelArea = pixelCount,
            TimestampMs = timestampMs
        });

        if (!coordinateMapper.TryMapToWorld(centroid, pixelCount, out Vector3 rawWorldPosition))
        {
            filter.Clear();
            GameEventBus.PublishTrackingLost(malletId, timestampMs);
            return TrackingLostState(malletId, timestampMs);
        }

        float confidence = coordinateMapper.ComputeConfidence(pixelCount, minPixelArea);
        if (!float.IsFinite(confidence) || confidence <= 0.0f)
        {
            filter.Clear();
            GameEventBus.PublishTrackingLost(malletId, timestampMs);
            return TrackingLostState(malletId, timestampMs);
        }

        filter.SetNoiseParameters(processNoise, measurementNoise);
        filter.Update(
            rawWorldPosition,
            timestampMs,
            out Vector3 smoothPosition,
            out Vector3 smoothVelocity);

        if (!KalmanFilter3D.IsFinite(smoothPosition) || !KalmanFilter3D.IsFinite(smoothVelocity))
        {
            filter.Clear();
            GameEventBus.PublishTrackingLost(malletId, timestampMs);
            return TrackingLostState(malletId, timestampMs);
        }

        return new MalletState
        {
            MalletId = malletId,
            Position = smoothPosition,
            Velocity = smoothVelocity,
            Confidence = confidence,
            TimestampMs = timestampMs
        };
    }

    internal MalletState ProcessLeftSampleForTesting(
        uint sumX,
        uint sumY,
        uint pixelCount,
        double timestampMs)
    {
        EnsureMapperInitializedForTesting();
        return ProcessSample(0, sumX, sumY, pixelCount, timestampMs, leftFilter);
    }

    internal void ConfigureMapperForTesting(
        float width,
        float height,
        bool frontFacing,
        int rotationDegrees = 0,
        bool verticalMirror = false)
    {
        coordinateMapper.SetupCamera(width, height, frontFacing, rotationDegrees, verticalMirror);
        coordinateMapper.UpdateCalibration(pixelMin, pixelMax, areaMin, areaMax, worldMin, worldMax);
        configuredWidth = Mathf.RoundToInt(Mathf.Max(1.0f, width));
        configuredHeight = Mathf.RoundToInt(Mathf.Max(1.0f, height));
        cameraCalibrationReady = true;
    }

    internal bool HasAnyPendingReadbackForTesting()
    {
        for (int i = 0; i < ReadbackSlotCount; i++)
        {
            if (readbackPending[i])
            {
                return true;
            }
        }
        return false;
    }

    internal void MarkReadbackPendingForTesting(int slot)
    {
        readbackPending[slot] = true;
    }

    internal void ReleaseResourcesForTesting()
    {
        shuttingDown = false;
        ReleaseResources();
    }

    private void EnsureMapperInitializedForTesting()
    {
        if (!cameraCalibrationReady)
        {
            ConfigureMapperForTesting(1280.0f, 720.0f, false);
        }
    }

    private static MalletState TrackingLostState(int malletId, double timestampMs)
    {
        return new MalletState
        {
            MalletId = malletId,
            Position = Vector3.zero,
            Velocity = Vector3.zero,
            Confidence = 0.0f,
            TimestampMs = timestampMs
        };
    }

    private void OnDisable()
    {
        GameEventBus.OnCalibrationCompleted -= ApplyCalibration;
        ReleaseResources();
    }

    private void OnDestroy()
    {
        ReleaseResources();
    }

    private void ReleaseResources()
    {
        if (shuttingDown)
        {
            return;
        }

        shuttingDown = true;
        if (sessionId != Guid.Empty && webCam != null && !terminalStatePublished)
        {
            PublishCameraInputState(CameraInputState.Stopped, CameraInputError.None);
        }

        for (int i = 0; i < ReadbackSlotCount; i++)
        {
            if (readbackPending[i])
            {
                if (readbackRequestActive[i] && !readbackRequests[i].done)
                {
                    readbackRequests[i].WaitForCompletion();
                }
                readbackPending[i] = false;
            }
            readbackRequestActive[i] = false;
            readbackRequests[i] = default;
            dispatchTimestamps[i] = 0.0;
            dispatchVersions[i] = 0;
            dispatchSessions[i] = Guid.Empty;
        }

        if (webCam != null)
        {
            webCam.Stop();
            webCam = null;
        }

        for (int i = 0; i < resultBuffers.Length; i++)
        {
            if (resultBuffers[i] != null)
            {
                resultBuffers[i].Release();
                resultBuffers[i] = null;
            }
        }

        leftFilter.Clear();
        rightFilter.Clear();
        filterVersion = 0;
        nextReadbackSlot = 0;
        sessionId = Guid.Empty;
        cameraCalibrationReady = false;
        configuredWidth = 0;
        configuredHeight = 0;
        cameraStreamingPublished = false;
    }

    private void PublishCameraInputState(
        CameraInputState state,
        CameraInputError error,
        string description = null)
    {
        PublishCameraInputState(state, error, description, Time.realtimeSinceStartupAsDouble * 1000.0);
    }

    private void PublishCameraInputState(
        CameraInputState state,
        CameraInputError error,
        string description,
        double changedAtMs)
    {
        GameEventBus.PublishCameraInputState(new CameraInputStateData
        {
            CameraId = cameraId,
            SessionId = sessionId,
            DeviceName = webCam == null ? cameraDeviceName : webCam.deviceName,
            State = state,
            ChangedAtMs = changedAtMs,
            Error = error,
            Description = description
        });
    }

    public void ApplyCalibration(
        Vector2 calibratedPixelMin,
        Vector2 calibratedPixelMax,
        double calibratedAreaMin,
        double calibratedAreaMax)
    {
        pixelMin = calibratedPixelMin;
        pixelMax = calibratedPixelMax;
        areaMin = calibratedAreaMin;
        areaMax = calibratedAreaMax;
        if (cameraCalibrationReady)
        {
            coordinateMapper.UpdateCalibration(
                pixelMin,
                pixelMax,
                areaMin,
                areaMax,
                worldMin,
                worldMax);
        }
    }
}
