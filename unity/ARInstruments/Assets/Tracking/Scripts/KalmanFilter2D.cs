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
        q = Mathf.Max(0.0f, processNoise);
        r = Mathf.Max(Mathf.Epsilon, measurementNoise);
    }

    public void Reset(Vector2 initialPosition)
    {
        position = initialPosition;
        velocity = Vector2.zero;
        p = 1.0f;
        lastTimestampMs = -1.0;
        isInitialized = true;
    }

    public void Reset(Vector2 initialPosition, double timestampMs)
    {
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
        if (!isInitialized || lastTimestampMs < 0.0)
        {
            Reset(measurement, timestampMs);
            smoothPosition = measurement;
            smoothVelocity = Vector2.zero;
            return;
        }

        float deltaTime = (float)((timestampMs - lastTimestampMs) / 1000.0);
        lastTimestampMs = timestampMs;
        if (deltaTime <= 0.001f)
        {
            deltaTime = 0.016f;
        }

        Vector2 predictedPosition = position + velocity * deltaTime;
        float predictedP = p + q;
        float gain = predictedP / (predictedP + r);
        Vector2 priorPosition = position;

        position = predictedPosition + gain * (measurement - predictedPosition);
        velocity = (position - priorPosition) / deltaTime;
        p = (1.0f - gain) * predictedP;

        smoothPosition = position;
        smoothVelocity = velocity;
    }
}
