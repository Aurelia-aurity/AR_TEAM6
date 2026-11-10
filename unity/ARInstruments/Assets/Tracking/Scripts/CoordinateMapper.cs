using UnityEngine;

/// <summary>
/// Maps camera pixel coordinates and contour area into Unity world coordinates.
/// </summary>
public sealed class CoordinateMapper
{
    private Vector2 cameraResolution = new Vector2(1920.0f, 1080.0f);
    private bool isFrontFacing = true;
    private Vector2 pixelMin = new Vector2(200.0f, 200.0f);
    private Vector2 pixelMax = new Vector2(1720.0f, 880.0f);
    private double areaMin = 500.0;
    private double areaMax = 2000.0;
    private Vector3 worldMin = new Vector3(-5.0f, -3.0f, 0.0f);
    private Vector3 worldMax = new Vector3(5.0f, 3.0f, 2.0f);

    public void SetupCamera(float width, float height, bool frontFacing)
    {
        cameraResolution = new Vector2(
            Mathf.Max(1.0f, width),
            Mathf.Max(1.0f, height));
        isFrontFacing = frontFacing;
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

    public Vector3 MapToWorld(Vector2 pixelPosition, double area)
    {
        float currentX = isFrontFacing
            ? cameraResolution.x - pixelPosition.x
            : pixelPosition.x;
        float normalizedX = SafeInverseLerp(pixelMin.x, pixelMax.x, currentX);
        float normalizedY = SafeInverseLerp(pixelMin.y, pixelMax.y, pixelPosition.y);
        float normalizedZ = SafeInverseLerp(
            (float)areaMin,
            (float)areaMax,
            (float)area);

        return new Vector3(
            Mathf.Lerp(worldMin.x, worldMax.x, normalizedX),
            Mathf.Lerp(worldMax.y, worldMin.y, normalizedY),
            Mathf.Lerp(worldMin.z, worldMax.z, normalizedZ));
    }

    public Vector3 MapVelocity(Vector3 normalizedVelocity)
    {
        return new Vector3(
            normalizedVelocity.x * (worldMax.x - worldMin.x),
            normalizedVelocity.y * (worldMax.y - worldMin.y),
            normalizedVelocity.z * (worldMax.z - worldMin.z));
    }

    private static float SafeInverseLerp(float start, float end, float value)
    {
        if (Mathf.Approximately(start, end))
        {
            return 0.0f;
        }

        return Mathf.Clamp01(Mathf.InverseLerp(start, end, value));
    }
}
