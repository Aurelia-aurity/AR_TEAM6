using UnityEngine;

/// <summary>Sample selection and tuning shared by instrument comparison and hit playback.</summary>
public abstract class InstrumentSoundProfile : ScriptableObject
{
    public const int StrengthCount = 3;
    public const float PlaybackGain = 0.35f;
    public const float MaxGain = 5f;
    public const float MinPitch = 0.5f;
    public const float MaxPitch = 1.5f;
    public abstract int NumberOfSamples { get; }
    public abstract int SecondarySampleOffset { get; }
    public virtual AudioClip GetReference(int strength) => null;
    public abstract AudioClip GetSample(int sample);
    public abstract float GetGain(int sample);
    public abstract float GetPitch(int sample);
    public abstract void SetGain(int sample, float gain);
    public abstract void SetPitch(int sample, float pitch);
}
