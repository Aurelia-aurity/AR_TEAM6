using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Native WebCamTexture and compute-shader tracker for both mallets.
/// Publishes normalized screen coordinates through the shared event bus.
/// </summary>
public sealed class GpuMalletTracker : MonoBehaviour
{
    public ComputeShader trackerShader;
    public RawImage cameraDebugger;

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

    private const int ResultCount = 6;
    private const int ThreadsPerGroup = 8;

    private WebCamTexture webCam;
    private ComputeBuffer resultBuffer;
    private readonly uint[] resultData = new uint[ResultCount];
    private int kernelIndex;
    private readonly KalmanFilter2D leftFilter = new KalmanFilter2D();
    private readonly KalmanFilter2D rightFilter = new KalmanFilter2D();

    private void Start()
    {
        if (trackerShader == null)
        {
            Debug.LogError("GpuMalletTracker requires a compute shader.", this);
            enabled = false;
            return;
        }

        if (!SystemInfo.supportsComputeShaders)
        {
            Debug.LogError("This platform does not support compute shaders.", this);
            enabled = false;
            return;
        }

        WebCamDevice[] devices = WebCamTexture.devices;
        if (devices == null || devices.Length == 0)
        {
            Debug.LogError("No camera device is available for GpuMalletTracker.", this);
            enabled = false;
            return;
        }

        WebCamDevice selectedDevice = devices[0];
        for (int i = 0; i < devices.Length; i++)
        {
            if (devices[i].isFrontFacing)
            {
                selectedDevice = devices[i];
                break;
            }
        }

        webCam = new WebCamTexture(selectedDevice.name, 1280, 720, 60);
        webCam.Play();
        if (cameraDebugger != null)
        {
            cameraDebugger.texture = webCam;
        }

        resultBuffer = new ComputeBuffer(ResultCount, sizeof(uint));
        kernelIndex = trackerShader.FindKernel("CSMain");
        leftFilter.SetNoiseParameters(processNoise, measurementNoise);
        rightFilter.SetNoiseParameters(processNoise, measurementNoise);
    }

    private void Update()
    {
        if (webCam == null || !webCam.isPlaying || !webCam.didUpdateThisFrame)
        {
            return;
        }

        ClearResultBuffer();
        trackerShader.SetTexture(kernelIndex, "InputTexture", webCam);
        trackerShader.SetVector("LeftLowerHSV", leftLowerHSV);
        trackerShader.SetVector("LeftUpperHSV", leftUpperHSV);
        trackerShader.SetVector("RightLowerHSV", rightLowerHSV);
        trackerShader.SetVector("RightUpperHSV", rightUpperHSV);
        trackerShader.SetBuffer(kernelIndex, "ResultBuffer", resultBuffer);

        int threadGroupsX = Mathf.CeilToInt(webCam.width / (float)ThreadsPerGroup);
        int threadGroupsY = Mathf.CeilToInt(webCam.height / (float)ThreadsPerGroup);
        trackerShader.Dispatch(kernelIndex, threadGroupsX, threadGroupsY, 1);
        resultBuffer.GetData(resultData);

        double timestampMs = Time.realtimeSinceStartup * 1000.0;
        MalletState leftState = CreateState(0, resultData[0], resultData[1], resultData[2], timestampMs, leftFilter);
        MalletState rightState = CreateState(1, resultData[3], resultData[4], resultData[5], timestampMs, rightFilter);

        if (leftState.Confidence <= 0.0f)
        {
            GameEventBus.OnTrackingLost?.Invoke(0);
        }

        if (rightState.Confidence <= 0.0f)
        {
            GameEventBus.OnTrackingLost?.Invoke(1);
        }

        GameEventBus.OnMalletsTracked?.Invoke(leftState, rightState);
    }

    private void ClearResultBuffer()
    {
        for (int i = 0; i < resultData.Length; i++)
        {
            resultData[i] = 0;
        }

        resultBuffer.SetData(resultData);
    }

    private MalletState CreateState(
        int malletId,
        uint sumX,
        uint sumY,
        uint pixelCount,
        double timestampMs,
        KalmanFilter2D filter)
    {
        if (pixelCount <= minPixelArea)
        {
            filter.Clear();
            return new MalletState
            {
                MalletId = malletId,
                Position = Vector3.zero,
                Velocity = Vector3.zero,
                Confidence = 0.0f,
                TimestampMs = timestampMs
            };
        }

        Vector2 centroid = new Vector2(
            sumX / (float)pixelCount,
            sumY / (float)pixelCount);
        Vector2 normalizedPosition = CorrectCameraRotation(centroid);
        filter.Update(
            normalizedPosition,
            timestampMs,
            out Vector2 smoothPosition,
            out Vector2 smoothVelocity);

        return new MalletState
        {
            MalletId = malletId,
            Position = new Vector3(smoothPosition.x, smoothPosition.y, 0.0f),
            Velocity = new Vector3(smoothVelocity.x, smoothVelocity.y, 0.0f),
            Confidence = 1.0f,
            TimestampMs = timestampMs
        };
    }

    private Vector2 CorrectCameraRotation(Vector2 pixelPosition)
    {
        float width = Mathf.Max(1.0f, webCam.width);
        float height = Mathf.Max(1.0f, webCam.height);
        float normalizedX = pixelPosition.x / width;
        float normalizedY = pixelPosition.y / height;

        switch (webCam.videoRotationAngle)
        {
            case 90:
                return new Vector2(1.0f - normalizedY, normalizedX);
            case 180:
                return new Vector2(1.0f - normalizedX, 1.0f - normalizedY);
            case 270:
                return new Vector2(normalizedY, 1.0f - normalizedX);
            default:
                return new Vector2(normalizedX, 1.0f - normalizedY);
        }
    }

    private void OnDestroy()
    {
        if (webCam != null)
        {
            webCam.Stop();
            webCam = null;
        }

        if (resultBuffer != null)
        {
            resultBuffer.Release();
            resultBuffer = null;
        }
    }
}
