using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A world-space 2D hit region. Audio and visual assets belong to subscribers.
/// </summary>
public sealed class InstrumentPad
{
    public string PadId;
    public Rect HitBox2D;
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
    private bool leftIsLocked;
    private bool rightIsLocked;
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
        }
    }

    public void Dispose()
    {
        Unsubscribe();
        ClearPads();
        leftIsLocked = false;
        rightIsLocked = false;
    }

    public void ProcessMalletStates(MalletState leftState, MalletState rightState)
    {
        ProcessMalletState(leftState);
        ProcessMalletState(rightState);
    }

    public void ProcessMalletState(MalletState state)
    {
        bool isLeft = state.MalletId == 0;
        bool isLocked = isLeft ? leftIsLocked : rightIsLocked;
        if (state.Confidence <= 0.0f)
        {
            SetLocked(isLeft, false);
            return;
        }

        float currentZ = state.Position.z;
        if (isLocked)
        {
            if (currentZ > LiftThresholdZ)
            {
                SetLocked(isLeft, false);
            }

            return;
        }

        float speed = state.Velocity.magnitude;
        if (currentZ >= HitThresholdZ || speed <= MinVelocityToTrigger)
        {
            return;
        }

        Vector2 position2D = new Vector2(state.Position.x, state.Position.y);
        for (int i = 0; i < activePads.Count; i++)
        {
            InstrumentPad pad = activePads[i];
            if (pad == null || !pad.HitBox2D.Contains(position2D))
            {
                continue;
            }

            float hitVolume = CalculateVolume(speed);
            GameEventBus.OnValidHit?.Invoke(new HitEvent
            {
                InstrumentId = pad.PadId,
                HitPosition = state.Position,
                HitForce = hitVolume,
                MalletId = state.MalletId
            });
            SetLocked(isLeft, true);
            return;
        }
    }

    private float CalculateVolume(float speed)
    {
        float speedRange = MaxVelocityToMap - MinVelocityToTrigger;
        if (speedRange <= Mathf.Epsilon)
        {
            return speed >= MaxVelocityToMap ? 1.0f : 0.0f;
        }

        return Mathf.Clamp01(
            (speed - MinVelocityToTrigger) / speedRange);
    }

    private void SetLocked(bool isLeft, bool value)
    {
        if (isLeft)
        {
            leftIsLocked = value;
        }
        else
        {
            rightIsLocked = value;
        }
    }
}
