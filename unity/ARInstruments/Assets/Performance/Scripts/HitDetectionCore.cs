using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A rectangular strike surface. Bounds are in surface-local XY, or world XY without a transform.
/// </summary>
public sealed class InstrumentPad
{
    public string PadId;
    public Rect HitBox2D;
    public Transform HitSurface;
}

/// <summary>
/// Module C hit detection. It consumes MalletState and publishes HitEvent.
/// </summary>
public sealed class HitDetectionCore : IDisposable
{
    public float HitThresholdZ { get; set; } = 0.2f;
    public float LiftThresholdZ { get; set; } = 0.5f;
    public float MinVelocityToTrigger { get; set; } = 1.5f;
    public float MaxVelocityToMap { get; set; } = 15.0f;

    private readonly List<InstrumentPad> activePads = new List<InstrumentPad>();
    private readonly MalletHitState leftMallet = new MalletHitState();
    private readonly MalletHitState rightMallet = new MalletHitState();
    private bool isSubscribed;

    public void RegisterPad(InstrumentPad pad)
    {
        if (pad == null)
        {
            return;
        }

        activePads.Add(pad);
    }

    public void ClearPads()
    {
        activePads.Clear();
        leftMallet.Unlock();
        rightMallet.Unlock();
        leftMallet.PreviousPosition = null;
        rightMallet.PreviousPosition = null;
    }

    public void Subscribe()
    {
        if (!isSubscribed)
        {
            GameEventBus.OnMalletsTracked += ProcessMalletStates;
            isSubscribed = true;
        }
    }

    public void Unsubscribe()
    {
        if (isSubscribed)
        {
            GameEventBus.OnMalletsTracked -= ProcessMalletStates;
            isSubscribed = false;
            leftMallet.PreviousPosition = null;
            rightMallet.PreviousPosition = null;
        }
    }

    public void Dispose()
    {
        Unsubscribe();
        ClearPads();
    }

    public void ProcessMalletStates(MalletState leftState, MalletState rightState)
    {
        ProcessMalletState(leftState);
        ProcessMalletState(rightState);
    }

    public void ProcessMalletState(MalletState state)
    {
        MalletHitState mallet = state.MalletId == 0 ? leftMallet : rightMallet;
        if (state.Confidence <= 0.0f)
        {
            // 추적이 재개되면 새 이동 구간부터 판정한다.
            mallet.PreviousPosition = null;
            return;
        }

        Vector3? previous = mallet.PreviousPosition;
        mallet.PreviousPosition = state.Position;
        if (mallet.IsWaitingForLift)
        {
            Transform surface = mallet.LockedPad.HitSurface;
            float liftPosition = state.Position.z;
            float liftThreshold = LiftThresholdZ;
            if (surface)
            {
                // 타격면 기준 거리
                liftPosition = Vector3.Dot(state.Position - surface.position, surface.forward);
                liftThreshold = LiftThresholdZ - HitThresholdZ;
            }

            if (liftPosition > liftThreshold)
            {
                mallet.Unlock();
            }

            return;
        }

        if (!previous.HasValue)
        {
            return;
        }

        for (int i = 0; i < activePads.Count; i++)
        {
            InstrumentPad pad = activePads[i];
            if (pad == null || !TryGetContact(pad, previous.Value, state, out Vector3 contact, out float inwardSpeed))
            {
                continue;
            }

            float normalizedHitStrength = CalculateNormalizedHitStrength(inwardSpeed);
            mallet.LockUntilLift(pad);
            GameEventBus.OnValidHit?.Invoke(new HitEvent
            {
                InstrumentId = pad.PadId,
                HitPosition = contact,
                HitForce = normalizedHitStrength,
                MalletId = state.MalletId
            });
            return;
        }
    }

    private bool TryGetContact(InstrumentPad pad, Vector3 previousWorld, MalletState state,
        out Vector3 contact, out float inwardSpeed)
    {
        Transform surface = pad.HitSurface;
        inwardSpeed = Vector3.Dot(state.Velocity, surface ? -surface.forward : Vector3.back);
        contact = default;
        bool hasEnoughInwardSpeed = inwardSpeed > MinVelocityToTrigger;
        if (!hasEnoughInwardSpeed)
        {
            return false;
        }

        Vector3 previous = surface ? surface.InverseTransformPoint(previousWorld) : previousWorld;
        Vector3 current = surface ? surface.InverseTransformPoint(state.Position) : state.Position;
        float planeZ = surface ? 0f : HitThresholdZ;
        bool crossesPlaneFromFront = previous.z > planeZ && current.z <= planeZ;
        if (!crossesPlaneFromFront)
        {
            return false;
        }

        float fraction = (previous.z - planeZ) / (previous.z - current.z);
        Vector3 localContact = Vector3.Lerp(previous, current, fraction);
        if (!pad.HitBox2D.Contains(new Vector2(localContact.x, localContact.y)))
        {
            return false;
        }

        contact = Vector3.Lerp(previousWorld, state.Position, fraction);
        return true;
    }

    private float CalculateNormalizedHitStrength(float speed)
    {
        float speedRange = MaxVelocityToMap - MinVelocityToTrigger;
        if (speedRange <= Mathf.Epsilon)
        {
            return speed >= MaxVelocityToMap ? 1.0f : 0.0f;
        }

        return Mathf.Clamp01(
            (speed - MinVelocityToTrigger) / speedRange);
    }

    private sealed class MalletHitState
    {
        public Vector3? PreviousPosition { get; set; }
        public InstrumentPad LockedPad { get; private set; }
        public bool IsWaitingForLift => LockedPad != null;

        public void LockUntilLift(InstrumentPad pad)
        {
            LockedPad = pad;
        }

        public void Unlock()
        {
            LockedPad = null;
        }
    }
}
