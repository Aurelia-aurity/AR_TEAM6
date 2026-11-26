using UnityEngine;

[CreateAssetMenu(menuName = "ARInstruments/Buk sound profile")]
public sealed class BukSoundProfile : InstrumentSoundProfile
{
    public const int SampleCount = 3;
    [SerializeField] private AudioClip[] clips = new AudioClip[SampleCount];
    [SerializeField] private Vector3 volumes = Vector3.one;
    [SerializeField] private Vector3 pitches = Vector3.one;
    public override int NumberOfSamples => SampleCount;
    public override int SecondarySampleOffset => 0;
    public override AudioClip GetSample(int sample) => sample >= 0 && sample < clips.Length ? clips[sample] : null;
    public override float GetGain(int sample) => volumes[sample];
    public override float GetPitch(int sample) => pitches[sample];
    public override void SetGain(int sample, float gain)
    {
        if (float.IsFinite(gain)) volumes[sample] = Mathf.Clamp(gain, 0f, MaxGain);
    }
    public override void SetPitch(int sample, float pitch)
    {
        if (float.IsFinite(pitch)) pitches[sample] = Mathf.Clamp(pitch, MinPitch, MaxPitch);
    }
}
