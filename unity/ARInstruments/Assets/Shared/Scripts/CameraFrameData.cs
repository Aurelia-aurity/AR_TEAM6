using System;
using UnityEngine;

/// <summary>A -> B: a newly received frame; the texture remains owned by A.</summary>
public struct CameraFrameData
{
    public string CameraId;
    public Guid SessionId;
    // A assigns this at acquisition, starting at 1 per camera session (0 for legacy frames).
    public long FrameSequence;
    public Texture Texture;
    // Clockwise degrees: 0, 90, 180 or 270. Vertical correction precedes rotation.
    public int ClockwiseRotation;
    public bool VerticallyMirrored;
    // Time.realtimeSinceStartupAsDouble * 1000 at receipt, NOT sensor capture time.
    public double ReceivedAtMs;
}
