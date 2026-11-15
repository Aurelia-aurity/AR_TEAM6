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
    public double MaxTrackingGapMs { get; set; } = 100.0;

    private readonly List<InstrumentPad> activePads = new List<InstrumentPad>();
    private readonly MalletHitState leftMallet = new MalletHitState();
    private readonly MalletHitState rightMallet = new MalletHitState();
    private bool isSubscribed;

    /// <summary>
    /// Adds a strike surface to check for hits.
    /// </summary>
    public void RegisterPad(InstrumentPad pad)
    {
        if (pad == null)
        {
            return;
        }

        activePads.Add(pad);
    }

    /// <summary>
    /// Removes all registered pads and resets both mallets.
    /// </summary>
    public void ClearPads()
    {
        activePads.Clear();
        leftMallet.Unlock();
        rightMallet.Unlock();
        leftMallet.ResetTracking();
        rightMallet.ResetTracking();
    }

    /// <summary>
    /// Subscribes to tracked mallet samples.
    /// </summary>
    public void Subscribe()
    {
        if (!isSubscribed)
        {
            GameEventBus.OnMalletsTracked += ProcessMalletStates;
            isSubscribed = true;
        }
    }

    /// <summary>
    /// Stops receiving tracked samples and clears sample history.
    /// </summary>
    public void Unsubscribe()
    {
        if (isSubscribed)
        {
            GameEventBus.OnMalletsTracked -= ProcessMalletStates;
            isSubscribed = false;
            leftMallet.ResetTracking();
            rightMallet.ResetTracking();
        }
    }

    /// <summary>
    /// Unsubscribes from tracking and clears pads and mallet state.
    /// </summary>
    public void Dispose()
    {
        Unsubscribe();
        ClearPads();
    }

    /// <summary>
    /// Processes the tracking samples for both mallets independently.
    /// </summary>
    public void ProcessMalletStates(MalletState leftState, MalletState rightState)
    {
        ProcessMalletState(leftState);
        ProcessMalletState(rightState);
    }

    /// <summary>
    /// Updates one mallet and publishes a hit when consecutive samples cross a registered surface at sufficient speed.
    /// </summary>
    public void ProcessMalletState(MalletState state)
    {
        MalletHitState mallet = state.MalletId == 0 ? leftMallet : rightMallet;
        if (!mallet.TryAcceptTimestamp(state.TimestampMs))
        {
            return;
        }

        if (state.Confidence <= 0.0f)
        {
            // 추적이 재개되면 새 이동 구간부터 판정한다.
            mallet.PreviousSample = null;
            return;
        }

        MalletState? previous = mallet.PreviousSample;
        mallet.PreviousSample = state;
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

        double elapsedMs = state.TimestampMs - previous.Value.TimestampMs;
        if (elapsedMs > MaxTrackingGapMs)
        {
            return;
        }

        Vector3 previousPosition = previous.Value.Position;
        Vector3 velocity = (state.Position - previousPosition) / (float)(elapsedMs / 1000.0);
        for (int i = 0; i < activePads.Count; i++)
        {
            InstrumentPad pad = activePads[i];
            if (pad == null || !TryGetContact(pad, previousPosition, state.Position, velocity, out Vector3 contact, out float inwardSpeed))
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

    /// <summary>
    /// Checks the inward speed and determines where the movement segment crosses the pad within its bounds.
    /// </summary>
    private bool TryGetContact(InstrumentPad pad, Vector3 previousWorld, Vector3 currentWorld, Vector3 velocity,
        out Vector3 contact, out float inwardSpeed)
    {
        Transform surface = pad.HitSurface;
        inwardSpeed = Vector3.Dot(velocity, surface ? -surface.forward : Vector3.back);
        contact = default;
        bool hasEnoughInwardSpeed = inwardSpeed > MinVelocityToTrigger;
        if (!hasEnoughInwardSpeed)
        {
            return false;
        }

        Vector3 previous = surface ? surface.InverseTransformPoint(previousWorld) : previousWorld;
        Vector3 current = surface ? surface.InverseTransformPoint(currentWorld) : currentWorld;
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

        contact = Vector3.Lerp(previousWorld, currentWorld, fraction);
        return true;
    }

    /// <summary>
    /// Maps the inward strike speed to a hit strength between 0 and 1.
    /// </summary>
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

    /// <summary>
    /// Stores the tracking history and strike lock for one mallet.
    /// </summary>
    private sealed class MalletHitState
    {
        public MalletState? PreviousSample { get; set; }
        private double? latestTimestampMs;
        public InstrumentPad LockedPad { get; private set; }
        public bool IsWaitingForLift => LockedPad != null;

        /// <summary>
        /// Records a finite timestamp if it is strictly newer than the last accepted timestamp.
        /// </summary>
        public bool TryAcceptTimestamp(double timestampMs)
        {
            bool hasInvalidTimestamp = double.IsNaN(timestampMs) || double.IsInfinity(timestampMs);
            bool isOutOfOrder = timestampMs <= latestTimestampMs;
            if (hasInvalidTimestamp || isOutOfOrder)
            {
                return false;
            }

            latestTimestampMs = timestampMs;
            return true;
        }

        /// <summary>
        /// Clears the stored sample and timestamp used for tracking.
        /// </summary>
        public void ResetTracking()
        {
            PreviousSample = null;
            latestTimestampMs = null;
        }

        /// <summary>
        /// Locks the mallet against further hits until it retreats from the struck pad.
        /// </summary>
        public void LockUntilLift(InstrumentPad pad)
        {
            LockedPad = pad;
        }

        /// <summary>
        /// Releases the strike lock so the mallet can hit again.
        /// </summary>
        public void Unlock()
        {
            LockedPad = null;
        }
    }
}
