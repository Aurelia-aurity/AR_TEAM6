using System;

public sealed class InstrumentPeakLimiter
{
    public const float Ceiling = 0.95f;
    private const float ReleaseSeconds = 0.05f;
    private float gain = 1f;

    public void Process(float[] samples, int channels, int sampleRate)
    {
        float release = (float)Math.Exp(-1.0 / (sampleRate * ReleaseSeconds));
        for (int frame = 0; frame < samples.Length; frame += channels)
        {
            float peak = 0f;
            for (int channel = 0; channel < channels; channel++)
            {
                peak = Math.Max(peak, Math.Abs(samples[frame + channel]));
            }

            float target = peak > Ceiling ? Ceiling / peak : 1f;
            // 모든 채널에 같은 감쇠를 적용하고, 피크가 지나가면 천천히 복원한다.
            gain = Math.Min(target, 1f - (1f - gain) * release);
            for (int channel = 0; channel < channels; channel++)
            {
                samples[frame + channel] *= gain;
            }
        }
    }
}
