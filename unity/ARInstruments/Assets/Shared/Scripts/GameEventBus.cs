using System;

// Contract: docs/event_bus_architecture_data_structures_ko.md.
// Publish on the Unity main thread; unsubscribe when components are disabled.
public static class GameEventBus
{
    // A -> B, C: left and right mallet states from one analyzed frame.
    public static Action<MalletState, MalletState> OnMalletsTracked;

    // Legacy notification without ordering information. Kept for existing subscribers.
    public static Action<int> OnTrackingLost;

    // A -> B: mallet ID and the failed observation's TimestampMs, using the same
    // clock as MalletState. Ordered consumers should use this instead of OnTrackingLost.
    public static Action<int, double> OnTrackingLostAt;

    public static void PublishTrackingLost(int malletId, double timestampMs)
    {
        OnTrackingLostAt?.Invoke(malletId, timestampMs);
        OnTrackingLost?.Invoke(malletId);
    }

    // A -> calibration: raw values required to build a CoordinateMapper calibration.
    public static Action<RawMalletSample> OnRawMalletSample;

    // C -> presentation and audio: one valid hit.
    public static Action<HitEvent> OnValidHit;
}
