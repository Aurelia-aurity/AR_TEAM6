using NUnit.Framework;
using UnityEngine;

public sealed class JangguSoundProfileTests
{
    [TestCase(0f, 2)]
    [TestCase(0.3299f, 2)]
    [TestCase(0.33f, 1)]
    [TestCase(0.6599f, 1)]
    [TestCase(0.66f, 0)]
    [TestCase(1f, 0)]
    public void HitForce_SelectsExpectedStrengthIndex(float force, int expectedIndex)
    {
        Assert.That(JangguSoundProfile.GetStrengthIndex(force), Is.EqualTo(expectedIndex));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    public void EditingOneSample_LeavesOtherSamplesUntouched(int selectedSample)
    {
        var profile = ScriptableObject.CreateInstance<JangguSoundProfile>();
        try
        {
            for (int sample = 0; sample < JangguSoundProfile.SampleCount; sample++)
            {
                profile.SetGain(sample, 0.5f + sample * 0.25f);
                profile.SetPitch(sample, 0.6f + sample * 0.1f);
            }

            profile.SetGain(selectedSample, 2.4f);
            Assert.That(profile.GetPitch(selectedSample), Is.EqualTo(0.6f + selectedSample * 0.1f));
            profile.SetPitch(selectedSample, 1.25f);

            for (int sample = 0; sample < JangguSoundProfile.SampleCount; sample++)
            {
                Assert.That(profile.GetGain(sample),
                    Is.EqualTo(sample == selectedSample ? 2.4f : 0.5f + sample * 0.25f), $"Gain at index {sample}");
                Assert.That(profile.GetPitch(sample),
                    Is.EqualTo(sample == selectedSample ? 1.25f : 0.6f + sample * 0.1f), $"Pitch at index {sample}");
            }
        }
        finally
        {
            Object.DestroyImmediate(profile);
        }
    }

    [TestCase(-1f, 0.25f, 0f, 0.5f)]
    [TestCase(9f, 2f, 5f, 1.5f)]
    public void SampleSettings_ClampOutOfRangeValues(float gain, float pitch, float expectedGain, float expectedPitch)
    {
        var profile = ScriptableObject.CreateInstance<JangguSoundProfile>();
        try
        {
            profile.SetGain(4, gain);
            profile.SetPitch(4, pitch);

            Assert.That(profile.GetGain(4), Is.EqualTo(expectedGain));
            Assert.That(profile.GetPitch(4), Is.EqualTo(expectedPitch));
        }
        finally
        {
            Object.DestroyImmediate(profile);
        }
    }
}
