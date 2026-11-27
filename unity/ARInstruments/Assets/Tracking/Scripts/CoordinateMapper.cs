using UnityEngine;

/// <summary>
/// Maps camera pixel coordinates and contour area into Unity world coordinates.
/// </summary>
public sealed class CoordinateMapper
{
    private Vector2 cameraResolution = new Vector2(1920.0f, 1080.0f);
    private bool isFrontFacing = true;
    private int clockwiseRotation;
    private bool verticallyMirrored;
    private Vector2 pixelMin = new Vector2(200.0f, 200.0f);
    private Vector2 pixelMax = new Vector2(1720.0f, 880.0f);
    private double areaMin = 500.0;
    private double areaMax = 2000.0;
    private Vector3 worldMin = new Vector3(-5.0f, -3.0f, 0.0f);
    private Vector3 worldMax = new Vector3(5.0f, 3.0f, 2.0f);

    public Vector2 PixelMin => pixelMin;
    public Vector2 PixelMax => pixelMax;
    public double AreaMin => areaMin;
    public double AreaMax => areaMax;

    public void SetupCamera(float width, float height, bool frontFacing)
    {
        SetupCamera(width, height, frontFacing, 0, false);
    }

    public void SetupCamera(
        float width,
        float height,
        bool frontFacing,
        int rotationDegrees,
        bool verticalMirror)
    {
        cameraResolution = new Vector2(
            Mathf.Max(1.0f, width),
            Mathf.Max(1.0f, height));
        isFrontFacing = frontFacing;
        SetOrientation(rotationDegrees, verticalMirror);
    }

    public void SetOrientation(int rotationDegrees, bool verticalMirror)
    {
        clockwiseRotation = rotationDegrees;
        verticallyMirrored = verticalMirror;
    }

    public void UpdateCalibration(
        Vector2 pMin,
        Vector2 pMax,
        double aMin,
        double aMax,
        Vector3 wMin,
        Vector3 wMax)
    {
        pixelMin = pMin;
        pixelMax = pMax;
        areaMin = aMin;
        areaMax = aMax;
        worldMin = wMin;
        worldMax = wMax;
    }

    /// <summary>
    /// Normalizes pixel coordinates in top-down space:
    /// applies VerticallyMirrored first, then ClockwiseRotation (0/90/180/270).
    /// Front-facing camera flag never flips world X (mirroring is Presentation-only).
    /// </summary>
    public Vector2 NormalizePixelPosition(Vector2 pixelPosition)
    {
        return NormalizePixelPosition(
            pixelPosition,
            cameraResolution.x,
            cameraResolution.y,
            clockwiseRotation,
            verticallyMirrored);
    }

    internal static Vector2 NormalizePixelPosition(
        Vector2 pixelPosition,
        float width,
        float height,
        int rotationDegrees,
        bool verticalMirror)
    {
        float x = pixelPosition.x;
        float y = verticalMirror
            ? height - pixelPosition.y
            : pixelPosition.y;

        int normalizedRotation = NormalizeRightAngle(rotationDegrees);
        switch (normalizedRotation)
        {
            case 90:
                return new Vector2(height - y, x);
            case 180:
                return new Vector2(width - x, height - y);
            case 270:
                return new Vector2(y, width - x);
            default:
                return new Vector2(x, y);
        }
    }

    public bool IsValidArea(double area)
    {
        return double.IsFinite(area)
            && area > 0.0
            && double.IsFinite(areaMin)
            && double.IsFinite(areaMax)
            && areaMin > 0.0
            && areaMax > areaMin
            && area >= areaMin
            && area <= areaMax;
    }

    public float ComputeConfidence(double area, uint minPixelArea = 0)
    {
        if (!IsValidArea(area) || area <= minPixelArea)
        {
            return 0.0f;
        }

        double span = areaMax - areaMin;
        float rangeRatio = Mathf.Clamp01((float)((area - areaMin) / span));
        double floorSpan = System.Math.Max(1.0, areaMax - minPixelArea);
        float floorRatio = Mathf.Clamp01((float)((area - minPixelArea) / floorSpan));
        return Mathf.Clamp(0.2f + 0.8f * (0.5f * rangeRatio + 0.5f * floorRatio), 0.05f, 1.0f);
    }

    public bool TryMapToWorld(Vector2 pixelPosition, double area, out Vector3 worldPosition)
    {
        if (!IsFinite(pixelPosition)
            || !IsFinite(cameraResolution)
            || !IsFinite(pixelMin)
            || !IsFinite(pixelMax)
            || !IsFinite(worldMin)
            || !IsFinite(worldMax)
            || clockwiseRotation % 90 != 0
            || !IsValidArea(area))
        {
            worldPosition = Vector3.zero;
            return false;
        }

        Vector2 orientedPixel = NormalizePixelPosition(pixelPosition);
        float normalizedX = SafeInverseLerp(pixelMin.x, pixelMax.x, orientedPixel.x);
        float normalizedY = SafeInverseLerp(pixelMin.y, pixelMax.y, orientedPixel.y);
        float normalizedZ = Mathf.Clamp01((float)((area - areaMin) / (areaMax - areaMin)));

        worldPosition = new Vector3(
            Mathf.Lerp(worldMin.x, worldMax.x, normalizedX),
            Mathf.Lerp(worldMax.y, worldMin.y, normalizedY),
            Mathf.Lerp(worldMin.z, worldMax.z, normalizedZ));

        if (!IsFinite(worldPosition))
        {
            worldPosition = Vector3.zero;
            return false;
        }

        return true;
    }

    public Vector3 MapToWorld(Vector2 pixelPosition, double area)
    {
        TryMapToWorld(pixelPosition, area, out Vector3 worldPosition);
        return worldPosition;
    }

    public Vector3 MapVelocity(Vector3 normalizedVelocity)
    {
        if (!IsFinite(normalizedVelocity) || !IsFinite(worldMin) || !IsFinite(worldMax))
        {
            return Vector3.zero;
        }

        return new Vector3(
            normalizedVelocity.x * (worldMax.x - worldMin.x),
            normalizedVelocity.y * (worldMax.y - worldMin.y),
            normalizedVelocity.z * (worldMax.z - worldMin.z));
    }

    internal static int NormalizeRightAngle(int rotationDegrees)
    {
        return ((rotationDegrees % 360) + 360) % 360;
    }

    private static float SafeInverseLerp(float start, float end, float value)
    {
        if (Mathf.Approximately(start, end))
        {
            return 0.0f;
        }

        return Mathf.Clamp01(Mathf.InverseLerp(start, end, value));
    }

    private static bool IsFinite(Vector2 value)
    {
        return float.IsFinite(value.x) && float.IsFinite(value.y);
    }

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    }
}
