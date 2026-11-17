using System.Collections.Generic;
using UnityEngine;

public sealed class AudioPoolManager : MonoBehaviour
{
    [Header("Audio Engine Settings")]
    [Tooltip("Maximum number of overlapping sounds.")]
    [SerializeField] private int maxPolyphony = 16;

    [Header("Instrument Assets")]
    [SerializeField] private string[] instrumentIds;
    [SerializeField] private AudioClip[] drumClips;

    private AudioSource[] audioSources;
    private Dictionary<string, AudioClip> clipDictionary;
    private int currentSourceIndex;

    private void Awake()
    {
        InitializeDictionary();
        InitializeAudioPool();
        PreloadAudioData();
    }

    private void OnEnable()
    {
        GameEventBus.OnValidHit += ProcessHitAndPlay;
    }

    private void OnDisable()
    {
        GameEventBus.OnValidHit -= ProcessHitAndPlay;
    }

    private void InitializeDictionary()
    {
        int idCount = instrumentIds == null ? 0 : instrumentIds.Length;
        int clipCount = drumClips == null ? 0 : drumClips.Length;
        int count = Mathf.Min(idCount, clipCount);

        clipDictionary = new Dictionary<string, AudioClip>(count);
        for (int i = 0; i < count; i++)
        {
            if (!string.IsNullOrEmpty(instrumentIds[i]) && drumClips[i] != null)
            {
                clipDictionary[instrumentIds[i]] = drumClips[i];
            }
        }
    }

    private void InitializeAudioPool()
    {
        int poolSize = Mathf.Max(1, maxPolyphony);
        audioSources = new AudioSource[poolSize];

        for (int i = 0; i < poolSize; i++)
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0.0f;
            source.dopplerLevel = 0.0f;
            audioSources[i] = source;
        }
    }

    private void PreloadAudioData()
    {
        if (drumClips == null)
        {
            return;
        }

        for (int i = 0; i < drumClips.Length; i++)
        {
            AudioClip clip = drumClips[i];
            if (clip != null && clip.loadState != AudioDataLoadState.Loaded)
            {
                clip.LoadAudioData();
            }
        }
    }

    private void ProcessHitAndPlay(HitEvent hitData)
    {
        if (string.IsNullOrEmpty(hitData.InstrumentId) ||
            clipDictionary == null ||
            !clipDictionary.TryGetValue(hitData.InstrumentId, out AudioClip clip))
        {
            return;
        }

        AudioSource source = audioSources[currentSourceIndex];
        source.clip = clip;
        source.volume = Mathf.Clamp01(hitData.HitForce);
        source.Play();

        currentSourceIndex++;
        if (currentSourceIndex >= audioSources.Length)
        {
            currentSourceIndex = 0;
        }
    }
}
