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
    private const int SampleCount = 60;

    public GpuMalletTracker tracker;
    public CalibrationStep CurrentStep { get; private set; }

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

    private void OnEnable()
    {
        GameEventBus.OnRawMalletSample += ProcessCalibrationSample;
    }

    private void OnDisable()
    {
        GameEventBus.OnRawMalletSample -= ProcessCalibrationSample;
    }

    public void Begin()
    {
        CurrentStep = CalibrationStep.NotStarted;
        StartNextStep();
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
        if (currentSampleIndex >= SampleCount)
        {
            RecordCurrentStep();
            StartNextStep();
        }
    }

    private void RecordCurrentStep()
    {
        Vector2 averagePixel = new Vector2(
            (float)(sumX / SampleCount),
            (float)(sumY / SampleCount));
        double averageArea = sumArea / SampleCount;

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
            if (tracker != null)
            {
                tracker.ApplyCalibration(minPixel, maxPixel, minArea, maxArea);
            }
        }
    }
}
