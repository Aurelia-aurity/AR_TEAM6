using UnityEngine;

/// <summary>
/// Lightweight constant-velocity Kalman filter for 3D world coordinates (meters, m/s).
/// </summary>
public sealed class KalmanFilter3D
{
    private Vector3 position;
    private Vector3 velocity;
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

    public void Reset(Vector3 initialPosition)
    {
        if (!IsFinite(initialPosition))
        {
            Clear();
            return;
        }

        position = initialPosition;
        velocity = Vector3.zero;
        p = 1.0f;
        lastTimestampMs = -1.0;
        isInitialized = true;
    }

    public void Reset(Vector3 initialPosition, double timestampMs)
    {
        if (!IsFinite(initialPosition) || !double.IsFinite(timestampMs))
        {
            Clear();
            return;
        }

        position = initialPosition;
        velocity = Vector3.zero;
        p = 1.0f;
        lastTimestampMs = timestampMs;
        isInitialized = true;
    }

    public void Clear()
    {
        isInitialized = false;
        lastTimestampMs = -1.0;
        position = Vector3.zero;
        velocity = Vector3.zero;
        p = 1.0f;
    }

    public void Update(
        Vector3 measurement,
        double timestampMs,
        out Vector3 smoothPosition,
        out Vector3 smoothVelocity)
    {
        if (!IsFinite(measurement) || !double.IsFinite(timestampMs))
        {
            smoothPosition = isInitialized ? position : Vector3.zero;
            smoothVelocity = Vector3.zero;
            return;
        }

        if (!isInitialized || lastTimestampMs < 0.0)
        {
            Reset(measurement, timestampMs);
            smoothPosition = position;
            smoothVelocity = Vector3.zero;
            return;
        }

        double deltaMs = timestampMs - lastTimestampMs;
        if (deltaMs <= 0.0)
        {
            smoothPosition = position;
            smoothVelocity = Vector3.zero;
            return;
        }

        float deltaTime = Mathf.Max((float)(deltaMs / 1000.0), 0.001f);
        lastTimestampMs = timestampMs;

        StepConstantVelocity(
            ref position,
            ref velocity,
            ref p,
            q,
            r,
            measurement,
            deltaTime);

        if (!IsFinite(position) || !IsFinite(velocity))
        {
            Clear();
            smoothPosition = Vector3.zero;
            smoothVelocity = Vector3.zero;
            return;
        }

        smoothPosition = position;
        smoothVelocity = velocity;
    }

    internal static void StepConstantVelocity(
        ref Vector3 position,
        ref Vector3 velocity,
        ref float p,
        float q,
        float r,
        Vector3 measurement,
        float deltaTime)
    {
        Vector3 predictedPosition = position + velocity * deltaTime;
        float predictedP = p + q;
        float gain = predictedP / (predictedP + r);
        Vector3 priorPosition = position;

        position = predictedPosition + gain * (measurement - predictedPosition);
        velocity = (position - priorPosition) / deltaTime;
        p = (1.0f - gain) * predictedP;
    }

    internal static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    }
}
