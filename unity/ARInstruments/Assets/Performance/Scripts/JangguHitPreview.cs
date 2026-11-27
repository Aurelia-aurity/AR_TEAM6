using UnityEngine;

/// <summary>Supplies the janggu model, region IDs and Deok/Kung sounds to the shared drum preview.</summary>
public sealed class JangguHitPreview : DrumHitPreview
{
    // Preserve the existing scene's serialized references and component GUID.
    [SerializeField] private Transform janggu;
    [SerializeField] private JangguAudioPreview hitAudio;

    protected override Transform InstrumentRoot => janggu;
    protected override string RegionA => "janggu_head_a_inner";
    protected override string RegionB => "janggu_head_b_inner";
    private InstrumentHitAudio hitPlayback;

    protected override void OnEnable()
    {
        hitPlayback = new InstrumentHitAudio(PlaySample,
            new InstrumentSoundBinding(RegionA, "Deok", 0, 1, 2),
            new InstrumentSoundBinding(RegionB, "Kung", 3, 4, 5));
        hitPlayback.Subscribe();
        base.OnEnable();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        hitPlayback?.Dispose();
        hitPlayback = null;
    }

    private void PlaySample(int sample)
    {
        if (hitAudio != null) hitAudio.PlaySample(sample);
    }

    protected override string GetHitSoundLabel(HitEvent hit) => hitPlayback?.GetLabel(hit);
}
