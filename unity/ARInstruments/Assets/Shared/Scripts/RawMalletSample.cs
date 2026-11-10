using UnityEngine;

// Module A -> calibration: raw camera-space measurements before world mapping.
public struct RawMalletSample
{
    public int MalletId;
    public Vector2 PixelPosition;
    public uint PixelArea;
    public double TimestampMs;
}
