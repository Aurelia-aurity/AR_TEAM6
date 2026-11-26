using UnityEngine;

/// <summary>Connects the Buk model and region IDs to the shared preview controls.</summary>
public sealed class BukHitPreview : DrumHitPreview
{
    [SerializeField] private Transform buk;
    protected override Transform InstrumentRoot => buk;
    protected override string RegionA => "buk_head_a_inner";
    protected override string RegionB => "buk_head_b_inner";
    protected override string GetHitSoundLabel(HitEvent hit) => "sound not configured";
}
