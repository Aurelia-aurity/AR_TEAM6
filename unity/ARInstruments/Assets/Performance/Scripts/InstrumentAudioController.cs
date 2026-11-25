using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static JangguSoundProfile;

/// <summary>
/// Runtime audio synthesizer subscribing to GameEventBus.OnValidHit.
/// Maintains a pooled voice bank and shared JangguOutputLimiter on active listeners.
/// </summary>
[DisallowMultipleComponent]
public sealed class InstrumentAudioController : MonoBehaviour
{
    private const int InitialVoicesPerSource = 8;

    [SerializeField] private JangguSoundProfile jangguProfile;
    [SerializeField] private AudioSource primarySource;
    [SerializeField] private AudioSource secondarySource;

    private readonly List<AudioSource> primaryVoices = new List<AudioSource>(InitialVoicesPerSource);
    private readonly List<AudioSource> secondaryVoices = new List<AudioSource>(InitialVoicesPerSource);
    private readonly List<JangguOutputLimiter> limiters = new List<JangguOutputLimiter>();
    private Coroutine pendingLimiterAcquisition;

    public bool IsAnyVoicePlaying
    {
        get
        {
            for (int i = 0; i < primaryVoices.Count; i++)
            {
                if (primaryVoices[i] != null && primaryVoices[i].isPlaying)
                {
                    return true;
                }
            }

            for (int i = 0; i < secondaryVoices.Count; i++)
            {
                if (secondaryVoices[i] != null && secondaryVoices[i].isPlaying)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private void Awake()
    {
        if (primarySource == null)
        {
            primarySource = CreateTemplateSource("PrimaryAudioSource");
        }

        if (secondarySource == null)
        {
            secondarySource = CreateTemplateSource("SecondaryAudioSource");
        }

        PrewarmVoices(primarySource, primaryVoices);
        PrewarmVoices(secondarySource, secondaryVoices);
    }

    private void OnEnable()
    {
        if (!AcquireLimiters())
        {
            pendingLimiterAcquisition = StartCoroutine(AcquireLimitersAfterRemoval());
        }

        GameEventBus.OnValidHit += OnValidHit;
        GameEventBus.OnSessionRunningChanged += OnSessionRunningChanged;
        GameEventBus.OnInstrumentSelected += OnInstrumentSelected;
    }

    private void OnDisable()
    {
        GameEventBus.OnValidHit -= OnValidHit;
        GameEventBus.OnSessionRunningChanged -= OnSessionRunningChanged;
        GameEventBus.OnInstrumentSelected -= OnInstrumentSelected;

        Stop();
        if (pendingLimiterAcquisition != null)
        {
            StopCoroutine(pendingLimiterAcquisition);
            pendingLimiterAcquisition = null;
        }

        for (int i = 0; i < limiters.Count; i++)
        {
            JangguOutputLimiter limiter = limiters[i];
            if (limiter != null && --limiter.PreviewUsers == 0)
            {
                limiter.PreviewUsers = -1;
                Destroy(limiter);
            }
        }

        limiters.Clear();
    }

    public void Stop()
    {
        for (int i = 0; i < primaryVoices.Count; i++)
        {
            if (primaryVoices[i] != null)
            {
                primaryVoices[i].Stop();
            }
        }

        for (int i = 0; i < secondaryVoices.Count; i++)
        {
            if (secondaryVoices[i] != null)
            {
                secondaryVoices[i].Stop();
            }
        }
    }

    private void OnSessionRunningChanged(bool isRunning)
    {
        if (!isRunning)
        {
            Stop();
        }
    }

    private void OnInstrumentSelected(InstrumentType _)
    {
        Stop();
    }

    private void OnValidHit(HitEvent hit)
    {
        if (!isActiveAndEnabled || jangguProfile == null || !GameEventBus.IsActiveContext(hit.ContextVersion)
            || string.IsNullOrEmpty(hit.InstrumentId))
        {
            return;
        }

        int strength = GetStrengthIndex(hit.HitForce);
        switch (hit.InstrumentId)
        {
            case "janggu_head_a_inner":
                PlaySample(primarySource, primaryVoices, strength, 1.0f);
                break;
            case "janggu_head_a_outer":
                PlaySample(primarySource, primaryVoices, strength, 1.06f);
                break;
            case "janggu_head_b_inner":
                PlaySample(secondarySource, secondaryVoices, StrengthCount + strength, 1.0f);
                break;
            case "janggu_head_b_outer":
                PlaySample(secondarySource, secondaryVoices, StrengthCount + strength, 1.05f);
                break;
            case "buk_head_a_inner":
            case "buk_head_b_inner":
                PlaySample(secondarySource, secondaryVoices, StrengthCount + strength, 0.78f);
                break;
            case "buk_head_a_outer":
            case "buk_head_b_outer":
                PlaySample(secondarySource, secondaryVoices, StrengthCount + strength, 0.92f);
                break;
        }
    }

    private void PlaySample(AudioSource template, List<AudioSource> voices, int sample, float pitchMultiplier)
    {
        AudioClip clip = jangguProfile.GetSample(sample);
        float gain = jangguProfile.GetGain(sample);
        float pitch = Mathf.Clamp(jangguProfile.GetPitch(sample) * pitchMultiplier, MinPitch, MaxPitch);
        Play(template, voices, clip, gain, pitch);
    }

    private void Play(AudioSource template, List<AudioSource> voices, AudioClip clip, float gain, float pitch)
    {
        if (!isActiveAndEnabled || template == null || clip == null || gain <= 0f)
        {
            return;
        }

        for (int i = 0; i < voices.Count; i++)
        {
            if (!voices[i].isPlaying)
            {
                PlayVoice(voices[i], clip, gain, pitch);
                return;
            }
        }

        PlayVoice(AddVoice(template, voices), clip, gain, pitch);
    }

    private AudioSource CreateTemplateSource(string sourceName)
    {
        var child = new GameObject(sourceName);
        child.transform.SetParent(transform, false);
        var source = child.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        return source;
    }

    private void PrewarmVoices(AudioSource template, List<AudioSource> voices)
    {
        if (template == null)
        {
            return;
        }

        for (int i = 0; i < InitialVoicesPerSource; i++)
        {
            AddVoice(template, voices);
        }
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

    private bool AcquireLimiters()
    {
        bool allAvailable = true;
        foreach (AudioListener listener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
        {
            if (!listener.isActiveAndEnabled)
            {
                continue;
            }

            var limiter = listener.GetComponent<JangguOutputLimiter>();
            if (limiter == null)
            {
                limiter = listener.gameObject.AddComponent<JangguOutputLimiter>();
                limiter.PreviewUsers = 1;
                limiters.Add(limiter);
            }
            else if (limiter.PreviewUsers < 0)
            {
                allAvailable = false;
            }
            else if (limiter.PreviewUsers > 0 && !limiters.Contains(limiter))
            {
                limiter.PreviewUsers++;
                limiters.Add(limiter);
            }
        }

        return allAvailable;
    }

    private IEnumerator AcquireLimitersAfterRemoval()
    {
        do
        {
            yield return null;
        }
        while (!AcquireLimiters());

        pendingLimiterAcquisition = null;
    }
}
