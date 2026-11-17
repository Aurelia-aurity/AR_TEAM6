using System.Collections.Generic;
using UnityEngine;

public sealed class JangguAudioPreview : MonoBehaviour
{
    private const int InitialVoicesPerSource = 8;

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

    private readonly List<AudioSource> primaryVoices = new List<AudioSource>(InitialVoicesPerSource);
    private readonly List<AudioSource> secondaryVoices = new List<AudioSource>(InitialVoicesPerSource);

    private void Awake()
    {
        PrewarmVoices(primarySource, primaryVoices);
        PrewarmVoices(secondarySource, secondaryVoices);
    }

    private void OnDisable()
    {
        Stop();
    }

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
        Play(primarySource, primaryVoices, deong[strength], 1f, 1f);
    }

    public void PlaySample(int sample)
    {
        AudioClip clip = sample < StrengthCount ? deok[sample] : kung[sample - StrengthCount];
        Play(primarySource, primaryVoices, clip, GetGain(sample), GetPitch(sample));
    }

    public void PlayPair(int selection)
    {
        int deokStrength = selection / StrengthCount;
        int kungStrength = selection % StrengthCount;
        int kungSample = StrengthCount + kungStrength;
        Play(primarySource, primaryVoices, deok[deokStrength], GetGain(deokStrength), GetPitch(deokStrength));
        Play(secondarySource, secondaryVoices, kung[kungStrength], GetGain(kungSample), GetPitch(kungSample));
    }

    public void Stop()
    {
        foreach (var voice in primaryVoices)
        {
            voice.Stop();
        }

        foreach (var voice in secondaryVoices)
        {
            voice.Stop();
        }
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

    private void PrewarmVoices(AudioSource template, List<AudioSource> voices)
    {
        if (template == null)
        {
            return;
        }

        for (int index = 0; index < InitialVoicesPerSource; index++)
        {
            AddVoice(template, voices);
        }
    }

    private void Play(AudioSource template, List<AudioSource> voices, AudioClip clip, float gain, float pitch)
    {
        if (template == null || clip == null || gain <= 0f)
        {
            return;
        }

        foreach (var voice in voices)
        {
            if (!voice.isPlaying)
            {
                PlayVoice(voice, clip, gain, pitch);
                return;
            }
        }

        // 준비한 소스가 모두 재생 중이면 새 소스를 추가해 이전 타격을 보존한다.
        PlayVoice(AddVoice(template, voices), clip, gain, pitch);
    }

    private AudioSource AddVoice(AudioSource template, List<AudioSource> voices)
    {
        var voice = Instantiate(template, transform);
        voice.name = $"{template.name} Voice {voices.Count + 1}";
        voice.playOnAwake = false;
        voice.clip = null;
        voices.Add(voice);
        return voice;
    }

    private static void PlayVoice(AudioSource voice, AudioClip clip, float gain, float pitch)
    {
        voice.volume = 1f;
        voice.pitch = pitch;
        voice.PlayOneShot(clip, gain);
    }
}
