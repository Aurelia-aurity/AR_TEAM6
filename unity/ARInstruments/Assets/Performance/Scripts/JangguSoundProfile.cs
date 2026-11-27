using UnityEngine;

public sealed class JangguSoundProfile : InstrumentSoundProfile
{
    public const int SampleCount = 6;

    [SerializeField] private AudioClip[] deong;
    [SerializeField] private AudioClip[] deok;
    [SerializeField] private AudioClip[] kung;

    // x, y, z는 강, 중, 약 순서다.
    [SerializeField] private Vector3 deokVolumes = Vector3.one;
    [SerializeField] private Vector3 kungVolumes = Vector3.one;
    [SerializeField] private Vector3 deokPitches = Vector3.one;
    [SerializeField] private Vector3 kungPitches = Vector3.one;

    public static int GetStrengthIndex(float normalizedForce) => InstrumentSoundBinding.GetStrengthIndex(normalizedForce);

    public override int NumberOfSamples => SampleCount;
    public override int SecondarySampleOffset => StrengthCount;
    public override AudioClip GetReference(int strength) => GetDeong(strength);

    public AudioClip GetDeong(int strength) => GetClip(deong, strength);
    public override AudioClip GetSample(int sample) => sample < StrengthCount
        ? GetClip(deok, sample)
        : GetClip(kung, sample - StrengthCount);

    public override float GetGain(int sample) => sample < StrengthCount
        ? deokVolumes[sample] : kungVolumes[sample - StrengthCount];

    public override float GetPitch(int sample) => sample < StrengthCount
        ? deokPitches[sample] : kungPitches[sample - StrengthCount];

    public override void SetGain(int sample, float gain)
    {
        SetSampleValue(ref deokVolumes, ref kungVolumes, sample, Mathf.Clamp(gain, 0f, MaxGain));
    }

    public override void SetPitch(int sample, float pitch)
    {
        SetSampleValue(ref deokPitches, ref kungPitches, sample,
            Mathf.Clamp(pitch, MinPitch, MaxPitch));
    }

    private static AudioClip GetClip(AudioClip[] clips, int index) =>
        clips != null && index < clips.Length ? clips[index] : null;

    private static void SetSampleValue(ref Vector3 deokValues, ref Vector3 kungValues, int sample, float value)
    {
        if (sample < StrengthCount)
        {
            deokValues[sample] = value;
        }
        else
        {
            kungValues[sample - StrengthCount] = value;
        }
    }
}
