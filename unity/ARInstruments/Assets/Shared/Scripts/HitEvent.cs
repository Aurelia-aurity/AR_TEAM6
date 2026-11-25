using UnityEngine;

// Module C publishes valid hits for audio and presentation subscribers.
public struct HitEvent
{
    public long ContextVersion;
    public string InstrumentId;
    public Vector3 HitPosition;
    public float HitForce;
    public int MalletId;
}
