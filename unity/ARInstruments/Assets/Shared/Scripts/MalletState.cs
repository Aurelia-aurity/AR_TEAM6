using UnityEngine;

// Module A publishes this state using the shared coordinate convention.
public struct MalletState
{
    public int MalletId;
    public Vector3 Position;
    public Vector3 Velocity;
    public float Confidence;
    public double TimestampMs;
}
