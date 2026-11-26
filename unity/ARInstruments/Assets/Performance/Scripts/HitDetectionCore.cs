using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Supported 2D strike boundary shapes in a surface transform's local XY plane.
/// </summary>
public enum PadShape
{
    Rectangle = 0,
    Circle = 1,
    Annulus = 2
}

/// <summary>
/// A strike surface. Bounds are in the required surface transform's local XY plane.
/// </summary>
public sealed class InstrumentPad
{
    public string PadId;
    public Rect HitBox2D;
    public Transform HitSurface;
    public PadShape Shape;
    public Vector2 Center;
    public float InnerRadius;
    public float OuterRadius;
}

/// <summary>
/// Module C hit detection. It consumes MalletState and publishes HitEvent.
/// </summary>
public sealed class HitDetectionCore : IDisposable
{
    private const float MinSurfaceScale = 1e-6f;

    /// <summary>Distance in world units the tip must retreat in front of the surface to strike again.</summary>
    public float RearmDistance { get; set; } = 0.3f;
    public float MinVelocityToTrigger { get; set; } = 1.5f;
    public float MaxVelocityToMap { get; set; } = 15.0f;
    public double MaxTrackingGapMs { get; set; } = 100.0;

    private readonly List<InstrumentPad> activePads = new List<InstrumentPad>();
    private readonly MalletHitState leftMallet = new MalletHitState();
    private readonly MalletHitState rightMallet = new MalletHitState();
    private bool isSubscribed;
    private long historyVersion;

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
        ResetTrackingState();
    }

    /// <summary>
    /// Clears tracking history and strike locks for both mallets while keeping registered pads.
    /// </summary>
    public void ResetTrackingState()
    {
        leftMallet.Unlock();
        rightMallet.Unlock();
        leftMallet.ResetTracking();
        rightMallet.ResetTracking();
    }

    /// <summary>
    /// Clears tracking history and strike lock for one valid mallet ID (0 or 1).
    /// </summary>
    public void ResetMallet(int malletId)
    {
        MalletHitState mallet = GetMalletState(malletId);
        if (mallet == null)
        {
            return;
        }

        mallet.Unlock();
        mallet.ResetTracking();
    }

    /// <summary>
    /// Subscribes to tracked mallet samples and tracking-lost notifications.
    /// </summary>
    public void Subscribe()
    {
        if (!isSubscribed)
        {
            GameEventBus.OnMalletsTracked += ProcessMalletStates;
            GameEventBus.OnTrackingLostAt += HandleTrackingLost;
            isSubscribed = true;
        }
    }

    /// <summary>
    /// Stops receiving tracked samples and clears sample history and strike locks.
    /// </summary>
    public void Unsubscribe()
    {
        if (isSubscribed)
        {
            GameEventBus.OnMalletsTracked -= ProcessMalletStates;
            GameEventBus.OnTrackingLostAt -= HandleTrackingLost;
            isSubscribed = false;
            ResetTrackingState();
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
        if (!GameEventBus.IsActiveContext(state.ContextVersion))
        {
            return;
        }

        MalletHitState mallet = GetMalletState(state.MalletId);
        if (mallet == null || !IsFinite(state.TimestampMs) || !IsFinite(state.Confidence))
        {
            return;
        }

        if (historyVersion != state.ContextVersion)
        {
            ResetTrackingState();
            historyVersion = state.ContextVersion;
        }

        if (!mallet.CanAcceptTimestamp(state.TimestampMs))
        {
            return;
        }

        if (state.Confidence <= 0.0f)
        {
            // 추적이 재개되면 새 이동 구간부터 판정한다.
            mallet.AcceptTimestamp(state.TimestampMs);
            mallet.PreviousSample = null;
            return;
        }

        if (!IsFinite(state.Position) || !IsFinite(state.Velocity))
        {
            return;
        }

        if (mallet.IsWaitingForLift)
        {
            mallet.AcceptTimestamp(state.TimestampMs);
            mallet.PreviousSample = state;
            TryUnlockAfterLift(mallet, state.Position);
            return;
        }

        MalletState? previous = mallet.PreviousSample;
        if (!previous.HasValue)
        {
            mallet.AcceptTimestamp(state.TimestampMs);
            mallet.PreviousSample = state;
            return;
        }

        double elapsedMs = state.TimestampMs - previous.Value.TimestampMs;
        if (elapsedMs > MaxTrackingGapMs)
        {
            mallet.AcceptTimestamp(state.TimestampMs);
            mallet.PreviousSample = state;
            return;
        }

        float dtSeconds = (float)(elapsedMs / 1000.0);
        if (dtSeconds <= 0f || !IsFinite(dtSeconds))
        {
            return;
        }

        Vector3 previousPosition = previous.Value.Position;
        Vector3 velocity = (state.Position - previousPosition) / dtSeconds;
        if (!IsFinite(velocity))
        {
            return;
        }

        mallet.AcceptTimestamp(state.TimestampMs);
        mallet.PreviousSample = state;

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
                ContextVersion = state.ContextVersion,
                InstrumentId = pad.PadId,
                HitPosition = contact,
                HitForce = normalizedHitStrength,
                MalletId = state.MalletId
            });
            return;
        }
    }

    private void HandleTrackingLost(int malletId, double timestampMs)
    {
        if (!IsFinite(timestampMs))
        {
            return;
        }

        MalletHitState mallet = GetMalletState(malletId);
        if (mallet == null || !mallet.IsTimestampAtOrAfterLatest(timestampMs))
        {
            return;
        }

        mallet.Unlock();
        mallet.PreviousSample = null;
        mallet.AcceptTimestamp(timestampMs);
    }

    private MalletHitState GetMalletState(int malletId)
    {
        if (malletId == 0)
        {
            return leftMallet;
        }

        if (malletId == 1)
        {
            return rightMallet;
        }

        return null;
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
        if (IsFinite(liftDistance) && liftDistance > RearmDistance)
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
        contact = default;
        inwardSpeed = CalculateInwardSpeed(velocity, surface);
        if (!HasValidSurfaceScale(surface) || !IsFinite(inwardSpeed) || inwardSpeed <= MinVelocityToTrigger)
        {
            return false;
        }

        Vector3 previous = surface.InverseTransformPoint(previousWorld);
        Vector3 current = surface.InverseTransformPoint(currentWorld);
        if (!IsFinite(previous) || !IsFinite(current))
        {
            return false;
        }

        // 채 끝점이 타격면 앞에서 출발해 면에 닿거나 통과했는지 확인한다.
        if (!TryGetCrossingFraction(previous.z, current.z, out float fraction))
        {
            return false;
        }

        // 타격면에 닿은 지점이 패드 범위 안인지 확인한다.
        if (!IsContactWithinPad(pad, previous, current, fraction))
        {
            return false;
        }

        contact = Vector3.Lerp(previousWorld, currentWorld, fraction);
        return IsFinite(contact);
    }

    private static bool HasValidSurfaceScale(Transform surface)
    {
        Vector3 scale = surface.lossyScale;
        return IsFinite(scale)
            && Mathf.Abs(scale.x) > MinSurfaceScale
            && Mathf.Abs(scale.y) > MinSurfaceScale
            && Mathf.Abs(scale.z) > MinSurfaceScale;
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

        float denom = previousZ - currentZ;
        if (denom <= 0f || !IsFinite(denom))
        {
            return false;
        }

        // 두 샘플 사이에서 타격면에 닿는 비율
        fraction = Mathf.Clamp01(previousZ / denom);
        return IsFinite(fraction);
    }

    /// <summary>
    /// Checks whether the intersection lies inside the pad's XY bounds.
    /// </summary>
    private static bool IsContactWithinPad(InstrumentPad pad, Vector3 previous, Vector3 current, float fraction)
    {
        Vector3 contact = Vector3.Lerp(previous, current, fraction);
        if (!IsFinite(contact))
        {
            return false;
        }

        Vector2 point = new Vector2(contact.x, contact.y);
        switch (pad.Shape)
        {
            case PadShape.Circle:
                return IsPointInCircle(point, pad.Center, pad.OuterRadius);
            case PadShape.Annulus:
                return IsPointInAnnulus(point, pad.Center, pad.InnerRadius, pad.OuterRadius);
            default:
                return IsPointInRect(point, pad.HitBox2D);
        }
    }

    private static bool IsPointInRect(Vector2 point, Rect bounds)
    {
        if (!IsFinite(bounds.x) || !IsFinite(bounds.y) || !IsFinite(bounds.width) || !IsFinite(bounds.height))
        {
            return false;
        }

        if (bounds.width <= 0f || bounds.height <= 0f)
        {
            return false;
        }

        return bounds.Contains(point);
    }

    private static bool IsPointInCircle(Vector2 point, Vector2 center, float outerRadius)
    {
        if (!IsFinite(center) || !IsFinite(outerRadius) || outerRadius <= 0f)
        {
            return false;
        }

        double dx = (double)point.x - center.x;
        double dy = (double)point.y - center.y;
        double distSq = dx * dx + dy * dy;
        double outerSq = (double)outerRadius * outerRadius;
        return distSq <= outerSq;
    }

    private static bool IsPointInAnnulus(Vector2 point, Vector2 center, float innerRadius, float outerRadius)
    {
        if (!IsFinite(center) || !IsFinite(innerRadius) || !IsFinite(outerRadius)
            || innerRadius < 0f || outerRadius <= innerRadius)
        {
            return false;
        }

        double dx = (double)point.x - center.x;
        double dy = (double)point.y - center.y;
        double distSq = dx * dx + dy * dy;
        double outerSq = (double)outerRadius * outerRadius;
        if (distSq > outerSq)
        {
            return false;
        }

        // Strict > on innerRadius prevents overlap with a Circle pad sharing OuterRadius == innerRadius.
        double innerSq = (double)innerRadius * innerRadius;
        return innerRadius <= 0f || distSq > innerSq;
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool IsFinite(Vector2 value) => IsFinite(value.x) && IsFinite(value.y);
    private static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

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
        /// Checks whether a finite timestamp is strictly newer than the last accepted timestamp.
        /// </summary>
        public bool CanAcceptTimestamp(double timestampMs)
        {
            if (!IsFinite(timestampMs))
            {
                return false;
            }

            return !latestTimestampMs.HasValue || timestampMs > latestTimestampMs.Value;
        }

        /// <summary>
        /// Checks whether a tracking-lost timestamp is at or newer than the last accepted timestamp.
        /// </summary>
        public bool IsTimestampAtOrAfterLatest(double timestampMs)
        {
            if (!IsFinite(timestampMs))
            {
                return false;
            }

            return !latestTimestampMs.HasValue || timestampMs >= latestTimestampMs.Value;
        }

        /// <summary>
        /// Records a timestamp that has already been validated.
        /// </summary>
        public void AcceptTimestamp(double timestampMs)
        {
            latestTimestampMs = timestampMs;
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
