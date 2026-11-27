using UnityEngine;

/// <summary>Connects the Buk model and region IDs to the shared preview controls.</summary>
public sealed class BukHitPreview : DrumHitPreview
{
    [SerializeField] private Transform buk;
    [SerializeField] private InstrumentSamplePlayer hitAudio;
    private InstrumentHitAudio hitPlayback;
    protected override Transform InstrumentRoot => buk;
    protected override string RegionA => "buk_head_a_inner";
    protected override string RegionB => "buk_head_b_inner";
    protected override void OnEnable()
    {
        hitPlayback = new InstrumentHitAudio(PlaySample,
            new InstrumentSoundBinding(RegionA, "Buk", 0, 1, 2),
            new InstrumentSoundBinding(RegionB, "Buk", 0, 1, 2));
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
