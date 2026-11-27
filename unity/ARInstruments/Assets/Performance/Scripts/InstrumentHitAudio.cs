using System;
using System.Collections.Generic;

/// <summary>Maps one region to strong, medium and weak samples in an instrument's player.</summary>
public readonly struct InstrumentSoundBinding
{
    public readonly string RegionId;
    public readonly string SoundName;
    private readonly int strongSample;
    private readonly int mediumSample;
    private readonly int weakSample;

    public InstrumentSoundBinding(string regionId, string soundName, int strongSample, int mediumSample, int weakSample)
    {
        RegionId = regionId;
        SoundName = soundName;
        this.strongSample = strongSample;
        this.mediumSample = mediumSample;
        this.weakSample = weakSample;
    }

    public static int GetStrengthIndex(float force) => force >= 0.66f ? 0 : force >= 0.33f ? 1 : 2;
    public int GetSample(float force) => GetStrengthIndex(force) == 0 ? strongSample
        : GetStrengthIndex(force) == 1 ? mediumSample : weakSample;
    public string GetLabel(float force) => $"{SoundName} {(GetStrengthIndex(force) == 0 ? "strong" : GetStrengthIndex(force) == 1 ? "medium" : "weak")}";
}

/// <summary>Routes valid hits to a sample player without depending on a preview, camera or audio UI.</summary>
public sealed class InstrumentHitAudio : IDisposable
{
    private readonly Dictionary<string, InstrumentSoundBinding> bindings = new Dictionary<string, InstrumentSoundBinding>(StringComparer.Ordinal);
    private readonly Action<int> playSample;
    private bool subscribed;

    public InstrumentHitAudio(Action<int> playSample, params InstrumentSoundBinding[] bindings)
    {
        this.playSample = playSample ?? throw new ArgumentNullException(nameof(playSample));
        if (bindings == null) throw new ArgumentNullException(nameof(bindings));
        foreach (InstrumentSoundBinding binding in bindings)
        {
            if (string.IsNullOrWhiteSpace(binding.RegionId)) throw new ArgumentException("A sound requires a region ID.", nameof(bindings));
            this.bindings.Add(binding.RegionId, binding);
        }
    }

    public void Subscribe()
    {
        if (subscribed) return;
        GameEventBus.OnValidHit += OnHit;
        subscribed = true;
    }

    public string GetLabel(HitEvent hit) => hit.InstrumentId != null && bindings.TryGetValue(hit.InstrumentId, out var binding)
        ? binding.GetLabel(hit.HitForce) : null;

    private void OnHit(HitEvent hit)
    {
        if (GameEventBus.IsActiveContext(hit.ContextVersion) && hit.InstrumentId != null
            && bindings.TryGetValue(hit.InstrumentId, out var binding))
            playSample(binding.GetSample(hit.HitForce));
    }

    public void Dispose()
    {
        if (!subscribed) return;
        GameEventBus.OnValidHit -= OnHit;
        subscribed = false;
    }
}
