using System.Collections.Generic;
using NUnit.Framework;

public sealed class InstrumentHitAudioTests
{
    [TestCase(1f, 10)]
    [TestCase(0.66f, 10)]
    [TestCase(0.65f, 11)]
    [TestCase(0.33f, 11)]
    [TestCase(0.32f, 12)]
    [TestCase(0f, 12)]
    public void ValidHit_SelectsConfiguredSampleWithoutPreview(float force, int expectedSample)
    {
        var played = new List<int>();
        using (var audio = new InstrumentHitAudio(played.Add,
            new InstrumentSoundBinding("buk_a", "Buk", 10, 11, 12)))
        {
            audio.Subscribe();
            audio.Subscribe();
            GameEventBus.OnValidHit?.Invoke(new HitEvent
            {
                ContextVersion = GameEventBus.ContextVersion, InstrumentId = "buk_a", HitForce = force
            });
            Assert.That(played, Is.EqualTo(new[] { expectedSample }));
        }
    }

    [Test]
    public void UnmappedAndStaleHits_AndDisposedSubscription_DoNotPlay()
    {
        var played = new List<int>();
        var audio = new InstrumentHitAudio(played.Add,
            new InstrumentSoundBinding("buk_b", "Buk", 3, 4, 5));
        using (audio)
        {
            audio.Subscribe();
            GameEventBus.OnValidHit?.Invoke(new HitEvent
            {
                ContextVersion = GameEventBus.ContextVersion, InstrumentId = "janggu_a", HitForce = 1f
            });
            GameEventBus.OnValidHit?.Invoke(new HitEvent
            {
                ContextVersion = GameEventBus.ContextVersion - 1, InstrumentId = "buk_b", HitForce = 1f
            });
            Assert.That(played, Is.Empty);
            Assert.That(audio.GetLabel(new HitEvent { InstrumentId = "buk_b", HitForce = 0.5f }), Is.EqualTo("Buk medium"));
            Assert.That(played, Is.Empty, "Reading a label must not trigger sound.");
        }
        GameEventBus.OnValidHit?.Invoke(new HitEvent
        {
            ContextVersion = GameEventBus.ContextVersion, InstrumentId = "buk_b", HitForce = 1f
        });
        Assert.That(played, Is.Empty);
    }
}
