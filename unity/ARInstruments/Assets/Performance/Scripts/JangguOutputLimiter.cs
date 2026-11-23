using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(AudioListener))]
public sealed class JangguOutputLimiter : MonoBehaviour
{
    private readonly JangguPeakLimiter limiter = new JangguPeakLimiter();
    private volatile int sampleRate;

    // Zero means scene-owned. Negative means scheduled for destruction.
    internal int PreviewUsers { get; set; }

    private void Awake()
    {
        sampleRate = AudioSettings.outputSampleRate;
    }

    private void OnEnable()
    {
        AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
    }

    private void OnDisable()
    {
        AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
    }

    private void OnAudioConfigurationChanged(bool deviceWasChanged)
    {
        sampleRate = AudioSettings.outputSampleRate;
    }

    private void OnAudioFilterRead(float[] data, int channels)
    {
        limiter.Process(data, channels, sampleRate);
    }
}
