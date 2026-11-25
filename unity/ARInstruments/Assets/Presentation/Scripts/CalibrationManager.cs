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

    public void Cancel()
    {
        CurrentStep = CalibrationStep.NotStarted;
        currentSampleIndex = 0;
        sumX = 0.0;
        sumY = 0.0;
        sumArea = 0.0;
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
}
