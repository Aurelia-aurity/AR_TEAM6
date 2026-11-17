using UnityEngine;

public sealed class JangguAudioPreview : MonoBehaviour
{
    public const int StrengthCount = 3;
    public const int SampleCount = 6;
    public const float MaxGain = 5f;
    public const float MinPitch = 0.5f;
    public const float MaxPitch = 1.5f;

    [SerializeField] private AudioClip[] deong;
    [SerializeField] private AudioClip[] deok;
    [SerializeField] private AudioClip[] kung;
    [SerializeField] private AudioSource primarySource;
    [SerializeField] private AudioSource secondarySource;

    // 각 벡터의 x, y, z 순서는 강, 중, 약이다.
    [SerializeField] private Vector3 deokVolumes = Vector3.one;
    [SerializeField] private Vector3 kungVolumes = Vector3.one;
    [SerializeField] private Vector3 deokPitches = Vector3.one;
    [SerializeField] private Vector3 kungPitches = Vector3.one;

    public float GetGain(int sample)
    {
        return sample < StrengthCount ? deokVolumes[sample] : kungVolumes[sample - StrengthCount];
    }

    public float GetPitch(int sample)
    {
        return sample < StrengthCount ? deokPitches[sample] : kungPitches[sample - StrengthCount];
    }

    public void SetGain(int sample, float gain)
    {
        SetSampleValue(ref deokVolumes, ref kungVolumes, sample, Mathf.Clamp(gain, 0f, MaxGain));
    }

    public void SetPitch(int sample, float pitch)
    {
        SetSampleValue(ref deokPitches, ref kungPitches, sample, Mathf.Clamp(pitch, MinPitch, MaxPitch));
    }

    public void PlayDeong(int strength)
    {
        Stop();
        Play(primarySource, deong[strength], 1f, 1f);
    }

    public void PlaySample(int sample)
    {
        Stop();
        AudioClip clip = sample < StrengthCount ? deok[sample] : kung[sample - StrengthCount];
        Play(primarySource, clip, GetGain(sample), GetPitch(sample));
    }

    public void PlayPair(int selection)
    {
        Stop();
        int deokStrength = selection / StrengthCount;
        int kungStrength = selection % StrengthCount;
        int kungSample = StrengthCount + kungStrength;
        Play(primarySource, deok[deokStrength], GetGain(deokStrength), GetPitch(deokStrength));
        Play(secondarySource, kung[kungStrength], GetGain(kungSample), GetPitch(kungSample));
    }

    public void Stop()
    {
        primarySource.Stop();
        secondarySource.Stop();
    }

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

    private static void Play(AudioSource source, AudioClip clip, float gain, float pitch)
    {
        if (clip == null)
        {
            return;
        }

        source.volume = 1f;
        source.pitch = pitch;
        source.PlayOneShot(clip, gain);
    }
}
