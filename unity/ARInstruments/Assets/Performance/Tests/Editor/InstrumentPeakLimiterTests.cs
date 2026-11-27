using System;
using NUnit.Framework;

public sealed class InstrumentPeakLimiterTests
{
    private const float Tolerance = 0.000001f;

    [TestCase(1)]
    [TestCase(2)]
    public void SignalBelowCeiling_PassesThroughUnchanged(int channels)
    {
        var samples = new[] { 0f, 0.2f, -0.4f, InstrumentPeakLimiter.Ceiling };
        var expected = (float[])samples.Clone();

        new InstrumentPeakLimiter().Process(samples, channels, 48000);

        Assert.That(samples, Is.EqualTo(expected));
    }

    [TestCase(1)]
    [TestCase(2)]
    public void OverloadedSignal_StaysWithinCeiling(int channels)
    {
        var samples = new[] { 4f, -2f, -8f, 3f, 0.5f, -0.5f };

        new InstrumentPeakLimiter().Process(samples, channels, 48000);

        foreach (float sample in samples)
        {
            Assert.That(float.IsNaN(sample), Is.False);
            Assert.That(Math.Abs(sample), Is.LessThanOrEqualTo(InstrumentPeakLimiter.Ceiling + Tolerance));
        }
        Assert.That(samples[0], Is.GreaterThan(0f));
        Assert.That(samples[1], Is.LessThan(0f));
    }

    [Test]
    public void StereoOverload_AppliesSameGainToBothChannels()
    {
        // 어느 채널이 더 크든 두 채널의 음량 비율을 유지한다.
        var samples = new[] { 4f, -2f, -1.5f, 3f, 0.4f, -0.2f };
        var original = (float[])samples.Clone();

        new InstrumentPeakLimiter().Process(samples, 2, 48000);

        for (int frame = 0; frame < samples.Length; frame += 2)
        {
            float leftGain = samples[frame] / original[frame];
            float rightGain = samples[frame + 1] / original[frame + 1];
            Assert.That(leftGain, Is.EqualTo(rightGain).Within(Tolerance));
            Assert.That(leftGain, Is.GreaterThan(0f).And.LessThan(1f));
        }
    }

    [TestCase(44100)]
    [TestCase(48000)]
    public void ProcessingSeparateBuffers_MatchesContinuousProcessing(int sampleRate)
    {
        var samples = new float[4096];
        for (int index = 0; index < samples.Length; index++)
        {
            samples[index] = (float)(2.5 * Math.Sin(index * 0.05));
        }
        var expected = (float[])samples.Clone();
        new InstrumentPeakLimiter().Process(expected, 2, sampleRate);

        var limiter = new InstrumentPeakLimiter();
        int offset = 0;
        foreach (int length in new[] { 2, 510, 1024, 2560 })
        {
            var buffer = new float[length];
            Array.Copy(samples, offset, buffer, 0, length);
            limiter.Process(buffer, 2, sampleRate);
            for (int index = 0; index < length; index++)
            {
                Assert.That(buffer[index], Is.EqualTo(expected[offset + index]).Within(Tolerance));
            }
            offset += length;
        }
        Assert.That(offset, Is.EqualTo(samples.Length));
    }

    [TestCase(44100)]
    [TestCase(48000)]
    public void AfterPeak_GainRecoversGraduallyAcrossBuffers(int sampleRate)
    {
        var limiter = new InstrumentPeakLimiter();
        limiter.Process(new[] { 2f }, 1, sampleRate);

        var recovery = new float[sampleRate / 2];
        for (int index = 0; index < recovery.Length; index++)
        {
            recovery[index] = 0.5f;
        }
        limiter.Process(recovery, 1, sampleRate);

        Assert.That(recovery[0], Is.InRange(0.23f, 0.25f));
        Assert.That(recovery[sampleRate / 20 - 1], Is.InRange(0.39f, 0.42f));
        Assert.That(recovery[recovery.Length - 1], Is.EqualTo(0.5f).Within(0.001f));
        for (int index = 1; index < recovery.Length; index++)
        {
            Assert.That(recovery[index], Is.GreaterThanOrEqualTo(recovery[index - 1]));
            Assert.That(recovery[index], Is.LessThanOrEqualTo(0.5f));
        }
    }

    [TestCase(1)]
    [TestCase(2)]
    public void SilenceAndEmptyBuffer_AreHandledWithoutErrors(int channels)
    {
        var limiter = new InstrumentPeakLimiter();
        Assert.DoesNotThrow(() => limiter.Process(Array.Empty<float>(), channels, 48000));

        var silence = new float[128];
        limiter.Process(silence, channels, 48000);

        Assert.That(silence, Is.All.EqualTo(0f));
    }
}
