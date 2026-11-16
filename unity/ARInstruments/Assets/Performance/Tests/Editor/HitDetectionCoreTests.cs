using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public sealed class HitDetectionCoreTests
{
    private readonly List<HitEvent> hits = new List<HitEvent>();
    private HitDetectionCore core;
    private GameObject surface;

    [SetUp]
    public void SetUp()
    {
        hits.Clear();
        core = new HitDetectionCore();
        surface = new GameObject("Strike Surface");
        core.RegisterPad(new InstrumentPad
        {
            PadId = "test_pad",
            HitSurface = surface.transform,
            HitBox2D = new Rect(-0.1f, -0.1f, 0.2f, 0.2f)
        });
        GameEventBus.OnValidHit += hits.Add;
    }

    [TearDown]
    public void TearDown()
    {
        GameEventBus.OnValidHit -= hits.Add;
        core.Dispose();
        Object.DestroyImmediate(surface);
    }

    [Test]
    public void CrossingRotatedSurface_ReportsWorldContactAndNormalizedStrength()
    {
        surface.transform.SetPositionAndRotation(new Vector3(2f, 1f, 3f), Quaternion.Euler(0f, 90f, 0f));
        Sample(0.15f, 0);
        Sample(-0.15f, 50);

        Assert.That(hits, Has.Count.EqualTo(1));
        Assert.That(hits[0].InstrumentId, Is.EqualTo("test_pad"));
        Assert.That(hits[0].MalletId, Is.EqualTo(0));
        Assert.That(Vector3.Distance(hits[0].HitPosition, surface.transform.position), Is.LessThan(0.0001f));
        Assert.That(hits[0].HitForce, Is.EqualTo(1f / 3f).Within(0.0001f));
    }

    [TestCase(-0.15f, 0.15f, 0f, 33)]
    [TestCase(0.15f, 0.05f, 0f, 33)]
    [TestCase(0.15f, -0.15f, 0.2f, 33)]
    [TestCase(0.03f, -0.03f, 0f, 100)]
    public void InvalidCrossing_DoesNotHit(float previousZ, float currentZ, float x, double elapsedMs)
    {
        Sample(previousZ, 0, x: x);
        Sample(currentZ, elapsedMs, x: x);

        Assert.That(hits, Is.Empty);
    }

    [Test]
    public void LockPreventsAnotherHitUntilMalletRetreats_AndDoesNotBlockOtherMallet()
    {
        Sample(0.4f, 0);
        Sample(-0.05f, 33);
        Sample(0.1f, 66);
        Sample(-0.05f, 99);
        Assert.That(hits, Has.Count.EqualTo(1));

        Sample(0.4f, 0, malletId: 1);
        Sample(-0.05f, 33, malletId: 1);
        Assert.That(hits, Has.Count.EqualTo(2));
        Assert.That(hits[1].MalletId, Is.EqualTo(1));

        Sample(0.4f, 132);
        Sample(-0.05f, 165);
        Assert.That(hits, Has.Count.EqualTo(3));
        Assert.That(hits[2].MalletId, Is.EqualTo(0));
    }

    [Test]
    public void LongGap_SkipsCrossingAndAllowsLaterValidHit()
    {
        Sample(0.4f, 0);
        Sample(-0.05f, 101);
        Assert.That(hits, Is.Empty);

        Sample(0.4f, 134);
        Sample(-0.05f, 167);
        Assert.That(hits, Has.Count.EqualTo(1));
    }

    [Test]
    public void TrackingLoss_DoesNotJoinSamplesAcrossTheGap()
    {
        Sample(0.4f, 0);
        Sample(0f, 33, confidence: 0f);
        Sample(-0.05f, 66);
        Assert.That(hits, Is.Empty);

        Sample(0.4f, 99);
        Sample(-0.05f, 132);
        Assert.That(hits, Has.Count.EqualTo(1));
    }

    private void Sample(float z, double timestampMs, float x = 0f, int malletId = 0, float confidence = 1f)
    {
        core.ProcessMalletState(new MalletState
        {
            MalletId = malletId,
            Position = surface.transform.TransformPoint(new Vector3(x, 0f, z)),
            Confidence = confidence,
            TimestampMs = timestampMs
        });
    }
}
