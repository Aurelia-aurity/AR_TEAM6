using UnityEngine;

/// <summary>
/// Lightweight constant-velocity Kalman filter for 2D screen coordinates.
/// </summary>
public sealed class KalmanFilter2D
{
    private Vector2 position;
    private Vector2 velocity;
    private float p = 1.0f;
    private float q = 0.05f;
    private float r = 0.5f;
    private double lastTimestampMs = -1.0;
    private bool isInitialized;

    public void SetNoiseParameters(float processNoise, float measurementNoise)
    {
        if (float.IsFinite(processNoise))
        {
            q = Mathf.Max(0.0f, processNoise);
        }

        if (float.IsFinite(measurementNoise))
        {
            r = Mathf.Max(Mathf.Epsilon, measurementNoise);
        }
    }

    public void Reset(Vector2 initialPosition)
    {
        if (!IsFinite(initialPosition))
        {
            Clear();
            return;
        }

        position = initialPosition;
        velocity = Vector2.zero;
        p = 1.0f;
        lastTimestampMs = -1.0;
        isInitialized = true;
    }

    public void Reset(Vector2 initialPosition, double timestampMs)
    {
        if (!IsFinite(initialPosition) || !double.IsFinite(timestampMs))
        {
            Clear();
            return;
        }

        position = initialPosition;
        velocity = Vector2.zero;
        p = 1.0f;
        lastTimestampMs = timestampMs;
        isInitialized = true;
    }

    public void Clear()
    {
        isInitialized = false;
        lastTimestampMs = -1.0;
        position = Vector2.zero;
        velocity = Vector2.zero;
        p = 1.0f;
    }

    public void Update(
        Vector2 measurement,
        double timestampMs,
        out Vector2 smoothPosition,
        out Vector2 smoothVelocity)
    {
        if (!IsFinite(measurement) || !double.IsFinite(timestampMs))
        {
            smoothPosition = isInitialized ? position : Vector2.zero;
            smoothVelocity = Vector2.zero;
            return;
        }

        if (!isInitialized || lastTimestampMs < 0.0)
        {
            Reset(measurement, timestampMs);
            smoothPosition = position;
            smoothVelocity = Vector2.zero;
            return;
        }

        double deltaMs = timestampMs - lastTimestampMs;
        if (deltaMs <= 0.0)
        {
            smoothPosition = position;
            smoothVelocity = Vector2.zero;
            return;
        }

        float deltaTime = Mathf.Max((float)(deltaMs / 1000.0), 0.001f);
        lastTimestampMs = timestampMs;

        Vector3 pos3 = new Vector3(position.x, position.y, 0.0f);
        Vector3 vel3 = new Vector3(velocity.x, velocity.y, 0.0f);
        KalmanFilter3D.StepConstantVelocity(
            ref pos3,
            ref vel3,
            ref p,
            q,
            r,
            new Vector3(measurement.x, measurement.y, 0.0f),
            deltaTime);

        position = new Vector2(pos3.x, pos3.y);
        velocity = new Vector2(vel3.x, vel3.y);

        if (!IsFinite(position) || !IsFinite(velocity))
        {
            Clear();
            smoothPosition = Vector2.zero;
            smoothVelocity = Vector2.zero;
            return;
        }

        smoothPosition = position;
        smoothVelocity = velocity;
    }

    private static bool IsFinite(Vector2 value)
    {
        return float.IsFinite(value.x) && float.IsFinite(value.y);
    }
}
