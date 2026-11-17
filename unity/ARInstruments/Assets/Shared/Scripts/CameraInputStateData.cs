using System;

public enum CameraInputState : byte
{
    Waiting,
    Streaming,
    Error,
    Stopped
}

public enum CameraInputError : byte
{
    None,
    DeviceNotFound,
    StartFailed,
    FrameTimeout
}

/// <summary>A -> B: input state, independent of mallet detection and calibration.</summary>
public struct CameraInputStateData
{
    // Stable logical ID (for example "primary"), not a physical device name.
    public string CameraId;
    // Guid.NewGuid() for every start attempt, including failed starts.
    public Guid SessionId;
    // Selected device name; empty until a device has been selected.
    public string DeviceName;
    public CameraInputState State;
    // Time.realtimeSinceStartupAsDouble * 1000 when the state changed.
    public double ChangedAtMs;
    public CameraInputError Error;
    public string Description;
}
