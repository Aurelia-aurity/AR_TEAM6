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

    protected override string PlayHitSound(HitEvent hit)
    {
        bool isDeok = hit.InstrumentId == RegionA;
        int strength = JangguSoundProfile.GetStrengthIndex(hit.HitForce);
        string strengthName = strength == 0 ? "strong" : strength == 1 ? "medium" : "weak";
        if (hitAudio != null)
        {
            int sample = isDeok ? strength : JangguSoundProfile.StrengthCount + strength;
            hitAudio.PlaySample(sample);
        }

        return $"{(isDeok ? "Deok" : "Kung")} {strengthName}";
    }
}
