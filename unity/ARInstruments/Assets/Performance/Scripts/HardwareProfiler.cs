using UnityEngine;

/// <summary>
/// Lightweight device timing monitor for Android soak tests.
/// Enable Frame Timing Stats in Player Settings before reading the values.
/// </summary>
public sealed class HardwareProfiler : MonoBehaviour
{
    [Min(1)]
    public int captureIntervalFrames = 30;
    [Min(1.0f)]
    public float gpuWarningThresholdMs = 13.0f;

    public double LastCpuFrameTimeMs { get; private set; }
    public double LastGpuFrameTimeMs { get; private set; }
    public double LastFrameTimeMs { get; private set; }

    private readonly FrameTiming[] frameTimings = new FrameTiming[1];
    private int frameCounter;
    private bool warningRaised;

    private void Start()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;
    }

    private void Update()
    {
        frameCounter++;
        if (frameCounter < captureIntervalFrames)
        {
            return;
        }

        frameCounter = 0;
        FrameTimingManager.CaptureFrameTimings();
        uint count = FrameTimingManager.GetLatestTimings(
            (uint)frameTimings.Length,
            frameTimings);
        if (count == 0)
        {
            return;
        }

        LastCpuFrameTimeMs = frameTimings[0].cpuFrameTime;
        LastGpuFrameTimeMs = frameTimings[0].gpuFrameTime;
        LastFrameTimeMs = frameTimings[0].cpuFrameTime;
        bool gpuSpike = LastGpuFrameTimeMs >= gpuWarningThresholdMs;
        if (gpuSpike && !warningRaised)
        {
            warningRaised = true;
            Debug.LogWarning(
                "HardwareProfiler: GPU frame time exceeded the configured threshold.",
                this);
        }
        else if (!gpuSpike)
        {
            warningRaised = false;
        }
    }
}
