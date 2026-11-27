using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using System.Linq;
using System.Reflection;

public sealed class BukSoundProfileTests
{
    [Test]
    public void IncludedProfile_MapsThreePcmSamplesAndUsesProjectTuning()
    {
        var profile = AssetDatabase.LoadAssetAtPath<BukSoundProfile>("Assets/Performance/Audio/BukSoundProfile.asset");
        Assert.That(profile, Is.Not.Null);
        Assert.That(profile.NumberOfSamples, Is.EqualTo(3));
        Assert.That(profile.SecondarySampleOffset, Is.Zero);
        for (int sample = 0; sample < 3; sample++)
        {
            AudioClip clip = profile.GetSample(sample);
            Assert.That(clip.name, Is.EqualTo("Jwago_" + (sample + 1)));
            clip.LoadAudioData();
            var data = new float[clip.samples * clip.channels];
            Assert.That(clip.GetData(data, 0), Is.True);
            Assert.That(profile.GetGain(sample), Is.EqualTo(3.9f));
            Assert.That(profile.GetPitch(sample), Is.EqualTo(1f));
        }
    }

    [Test]
    public void Tuning_ClampsAndRejectsNonFiniteInput()
    {
        var profile = ScriptableObject.CreateInstance<BukSoundProfile>();
        try
        {
            profile.SetGain(0, 99f);
            profile.SetPitch(1, 0f);
            profile.SetGain(0, float.NaN);
            profile.SetPitch(1, float.PositiveInfinity);
            Assert.That(profile.GetGain(0), Is.EqualTo(5f));
            Assert.That(profile.GetPitch(1), Is.EqualTo(0.5f));
        }
        finally { Object.DestroyImmediate(profile); }
    }

    [TestCase(1f)]
    [TestCase(3.9f)]
    [TestCase(5f)]
    public void ActualEditorMix_AllBukPairsStayWithinOutputCeiling(float gain)
    {
        var profile = AssetDatabase.LoadAssetAtPath<BukSoundProfile>("Assets/Performance/Audio/BukSoundProfile.asset");
        var type = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("InstrumentSoundProfileEditor"))
            .First(t => t != null);
        Editor editor = Editor.CreateEditor(profile, type);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        try
        {
            for (int a = 0; a < 3; a++)
                for (int b = 0; b < 3; b++)
                {
                    object[] arguments = { profile.GetSample(a), gain * InstrumentSoundProfile.PlaybackGain,
                        profile.GetSample(b), gain * InstrumentSoundProfile.PlaybackGain, 1f, 1f, null };
                    var clip = (AudioClip)type.GetMethod("ImportPreviewMix", flags).Invoke(editor, arguments);
                    Assert.That(clip, Is.Not.Null, arguments[6] as string);
                    var samples = new float[clip.samples * clip.channels];
                    Assert.That(clip.GetData(samples, 0), Is.True);
                    Assert.That(samples.Max(Mathf.Abs), Is.LessThanOrEqualTo(InstrumentPeakLimiter.Ceiling + 0.0001f));
                    type.GetMethod("StopPlayback", flags).Invoke(editor, null);
                }
        }
        finally { Object.DestroyImmediate(editor); }
    }

    [Test]
    public void ProjectTuning_CompleteSinglesPairsAndRapidHitsStayWithinLimiterCeiling()
    {
        var profile = AssetDatabase.LoadAssetAtPath<BukSoundProfile>("Assets/Performance/Audio/BukSoundProfile.asset");
        var pcm = new float[3][];
        AudioClip reference = profile.GetSample(0);
        for (int i = 0; i < 3; i++)
        {
            AudioClip clip = profile.GetSample(i);
            Assert.That(clip.frequency, Is.EqualTo(reference.frequency));
            Assert.That(clip.channels, Is.EqualTo(reference.channels));
            Assert.That(profile.GetPitch(i), Is.EqualTo(1f));
            pcm[i] = new float[clip.samples * clip.channels];
            Assert.That(clip.GetData(pcm[i], 0), Is.True);
            for (int sample = 0; sample < pcm[i].Length; sample++)
                pcm[i][sample] *= profile.GetGain(i) * InstrumentSoundProfile.PlaybackGain;
            AssertLimited("single " + i, pcm[i], reference.channels, reference.frequency);
        }
        for (int a = 0; a < 3; a++)
            for (int b = 0; b < 3; b++)
            {
                var mix = new float[System.Math.Max(pcm[a].Length, pcm[b].Length)];
                Add(mix, pcm[a], 0);
                Add(mix, pcm[b], 0);
                AssertLimited($"pair {a}+{b}", mix, reference.channels, reference.frequency);
            }
        int interval = Mathf.RoundToInt(reference.frequency * 0.1f) * reference.channels;
        var burst = new float[pcm[0].Length + interval * 9];
        for (int hit = 0; hit < 10; hit++)
        {
            Add(burst, pcm[0], hit * interval);
            Add(burst, pcm[0], hit * interval);
        }
        AssertLimited("10 strong pairs at 100ms", burst, reference.channels, reference.frequency);
    }

    private static void Add(float[] destination, float[] source, int offset)
    {
        for (int i = 0; i < source.Length; i++) destination[i + offset] += source[i];
    }

    private static void AssertLimited(string label, float[] raw, int channels, int rate)
    {
        var limiter = new InstrumentPeakLimiter();
        float before = raw.Max(Mathf.Abs);
        float after = 0;
        for (int offset = 0; offset < raw.Length; offset += 512 * channels)
        {
            var block = new float[System.Math.Min(512 * channels, raw.Length - offset)];
            System.Array.Copy(raw, offset, block, 0, block.Length);
            limiter.Process(block, channels, rate);
            Assert.That(block.All(float.IsFinite), Is.True, label);
            after = Mathf.Max(after, block.Max(Mathf.Abs));
        }
        Assert.That(after, Is.GreaterThan(0f), label);
        Assert.That(after, Is.LessThanOrEqualTo(InstrumentPeakLimiter.Ceiling + 0.00001f), label);
        TestContext.Out.WriteLine($"{label}: raw={before:F6}, limited={after:F6}");
    }
}
