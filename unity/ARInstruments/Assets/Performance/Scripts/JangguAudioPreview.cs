using System.Collections.Generic;
using UnityEngine;
using static JangguSoundProfile;

public sealed class JangguAudioPreview : MonoBehaviour
{
    private const int InitialVoicesPerSource = 8;

    [SerializeField] private JangguSoundProfile profile;
    [SerializeField] private AudioSource primarySource;
    [SerializeField] private AudioSource secondarySource;

    private JangguSoundProfile sessionProfile;
    private int editedGains;
    private int editedPitches;

    public bool KeepSessionTuning { get; set; }
    public JangguSoundProfile SavedProfile => profile;
    public JangguSoundProfile Profile => sessionProfile != null ? sessionProfile : profile;

    private readonly List<AudioSource> primaryVoices = new List<AudioSource>(InitialVoicesPerSource);
    private readonly List<AudioSource> secondaryVoices = new List<AudioSource>(InitialVoicesPerSource);

    private void Awake()
    {
        foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
        {
            if (listener.isActiveAndEnabled && listener.GetComponent<JangguOutputLimiter>() == null)
            {
                listener.gameObject.AddComponent<JangguOutputLimiter>();
            }
        }

        if (profile != null)
        {
            sessionProfile = Instantiate(profile);
        }

        PrewarmVoices(primarySource, primaryVoices);
        PrewarmVoices(secondarySource, secondaryVoices);
    }

    private void OnDisable()
    {
        Stop();
    }

    private void OnDestroy()
    {
        if (sessionProfile != null)
        {
            Destroy(sessionProfile);
        }
    }

    public float GetGain(int sample) => Profile != null ? Profile.GetGain(sample) : 1f;
    public float GetPitch(int sample) => Profile != null ? Profile.GetPitch(sample) : 1f;
    public void SetGain(int sample, float gain)
    {
        if (sessionProfile != null)
        {
            sessionProfile.SetGain(sample, gain);
            editedGains |= 1 << sample;
        }
    }

    public void SetPitch(int sample, float pitch)
    {
        if (sessionProfile != null)
        {
            sessionProfile.SetPitch(sample, pitch);
            editedPitches |= 1 << sample;
        }
    }

    public bool SaveSessionTuning()
    {
        if (!KeepSessionTuning || sessionProfile == null || profile == null ||
            (editedGains | editedPitches) == 0)
        {
            return false;
        }

        for (int sample = 0; sample < SampleCount; sample++)
        {
            int mask = 1 << sample;
            if ((editedGains & mask) != 0)
            {
                profile.SetGain(sample, sessionProfile.GetGain(sample));
            }

            if ((editedPitches & mask) != 0)
            {
                profile.SetPitch(sample, sessionProfile.GetPitch(sample));
            }
        }

        return true;
    }

    public void PlayDeong(int strength)
    {
        Play(primarySource, primaryVoices, Profile?.GetDeong(strength), 1f, 1f);
    }

    public void PlaySample(int sample)
    {
        AudioClip clip = Profile?.GetSample(sample);
        Play(primarySource, primaryVoices, clip, GetGain(sample), GetPitch(sample));
    }

    public void PlayPair(int selection)
    {
        int deokStrength = selection / StrengthCount;
        int kungStrength = selection % StrengthCount;
        int kungSample = StrengthCount + kungStrength;
        Play(primarySource, primaryVoices, Profile?.GetSample(deokStrength), GetGain(deokStrength), GetPitch(deokStrength));
        Play(secondarySource, secondaryVoices, Profile?.GetSample(kungSample), GetGain(kungSample), GetPitch(kungSample));
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
        voice.volume = PlaybackGain;
        voice.pitch = pitch;
        voice.PlayOneShot(clip, gain);
    }
}
