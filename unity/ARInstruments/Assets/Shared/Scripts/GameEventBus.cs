using System;

// Contract: docs/event_bus_architecture_data_structures_ko.md.
// Publish on the Unity main thread; unsubscribe when components are disabled.
public static class GameEventBus
{
    // A -> B, C: left and right mallet states from one analyzed frame.
    public static Action<MalletState, MalletState> OnMalletsTracked;

    // A -> B, C: ID of the mallet whose tracking was lost.
    public static Action<int> OnTrackingLost;

    // C -> presentation and audio: one valid hit.
    public static Action<HitEvent> OnValidHit;
}
