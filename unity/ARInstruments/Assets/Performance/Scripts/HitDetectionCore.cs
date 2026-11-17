using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A rectangular strike surface. Bounds are in the required surface transform's local XY plane.
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
    /// <summary>Distance in world units the tip must retreat in front of the surface to strike again.</summary>
    public float RearmDistance { get; set; } = 0.3f;
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
    /// <exception cref="ArgumentNullException">The pad is null.</exception>
    /// <exception cref="ArgumentException">The pad has no live surface transform.</exception>
    public void RegisterPad(InstrumentPad pad)
    {
        if (pad == null)
        {
            throw new ArgumentNullException(nameof(pad));
        }

        if (!pad.HitSurface)
        {
            throw new ArgumentException("The pad requires a valid hit surface.", nameof(pad));
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

        if (float.IsNaN(state.Confidence) || float.IsInfinity(state.Confidence) || state.Confidence <= 0.0f
            || float.IsNaN(state.Position.x) || float.IsInfinity(state.Position.x)
            || float.IsNaN(state.Position.y) || float.IsInfinity(state.Position.y)
            || float.IsNaN(state.Position.z) || float.IsInfinity(state.Position.z))
        {
            // 추적이 재개되면 새 이동 구간부터 판정한다.
            mallet.PreviousSample = null;
            return;
        }

        MalletState? previous = mallet.PreviousSample;
        mallet.PreviousSample = state;
        if (mallet.IsWaitingForLift)
        {
            TryUnlockAfterLift(mallet, state.Position);
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
            if (!pad.HitSurface || !TryGetContact(pad, previousPosition, state.Position, velocity, out Vector3 contact, out float inwardSpeed))
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
    /// Releases the strike lock once the tip has retreated far enough.
    /// </summary>
    private void TryUnlockAfterLift(MalletHitState mallet, Vector3 tipPosition)
    {
        Transform surface = mallet.LockedPad.HitSurface;
        if (!surface)
        {
            // 타격면이 제거되면 해당 면에 걸린 잠금을 해제한다.
            mallet.Unlock();
            return;
        }

        // 타격면 기준 거리. 면 앞쪽이 양수다.
        float liftDistance = Vector3.Dot(tipPosition - surface.position, surface.forward);
        if (liftDistance > RearmDistance)
        {
            mallet.Unlock();
        }
    }

    /// <summary>
    /// Checks the inward speed and determines where the movement segment crosses the pad within its bounds.
    /// </summary>
    private bool TryGetContact(InstrumentPad pad, Vector3 previousWorld, Vector3 currentWorld, Vector3 velocity,
        out Vector3 contact, out float inwardSpeed)
    {
        Transform surface = pad.HitSurface;
        inwardSpeed = CalculateInwardSpeed(velocity, surface);
        contact = default;
        bool hasEnoughInwardSpeed = inwardSpeed > MinVelocityToTrigger;
        if (!hasEnoughInwardSpeed)
        {
            return false;
        }

        Vector3 previous = surface.InverseTransformPoint(previousWorld);
        Vector3 current = surface.InverseTransformPoint(currentWorld);
        // 채 끝점이 타격면 앞에서 출발해 면에 닿거나 통과했는지 확인한다.
        if (!TryGetCrossingFraction(previous.z, current.z, out float fraction))
        {
            return false;
        }

        // 타격면에 닿은 지점이 패드 범위 안인지 확인한다.
        if (!IsContactWithinPad(pad.HitBox2D, previous, current, fraction))
        {
            return false;
        }

        contact = Vector3.Lerp(previousWorld, currentWorld, fraction);
        return true;
    }

    /// <summary>
    /// Returns the velocity component directed into the strike surface.
    /// </summary>
    private static float CalculateInwardSpeed(Vector3 velocity, Transform surface)
    {
        Vector3 inwardDirection = -surface.forward;
        return Vector3.Dot(velocity, inwardDirection);
    }

    /// <summary>
    /// Finds where a movement segment reaches the plane from the front.
    /// </summary>
    private static bool TryGetCrossingFraction(float previousZ, float currentZ, out float fraction)
    {
        fraction = default;
        bool crossesPlaneFromFront = previousZ > 0f && currentZ <= 0f;
        if (!crossesPlaneFromFront)
        {
            return false;
        }

        // 두 샘플 사이에서 타격면에 닿는 비율
        fraction = previousZ / (previousZ - currentZ);
        return true;
    }

    /// <summary>
    /// Checks whether the intersection lies inside the pad's XY bounds.
    /// </summary>
    private static bool IsContactWithinPad(Rect bounds, Vector3 previous, Vector3 current, float fraction)
    {
        Vector3 contact = Vector3.Lerp(previous, current, fraction);
        return bounds.Contains(new Vector2(contact.x, contact.y));
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
