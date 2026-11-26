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

    [Test]
    public void CrossingRequiresTwoActiveSamplesAndPreservesTheirVersion()
    {
        try
        {
            Sample(0.4f, 0);
            GameEventBus.BeginContext(1);
            Sample(-0.05f, 20, version: 1);
            GameEventBus.ActivateContext(1);
            Sample(-0.05f, 33, version: 1);
            Sample(0.4f, 44, version: 1);
            Sample(-0.05f, 55);
            Assert.That(hits, Is.Empty);
            Sample(-0.05f, 66, version: 1);
            Assert.That(hits, Has.Count.EqualTo(1));
            Assert.That(hits[0].ContextVersion, Is.EqualTo(1));
        }
        finally
        {
            typeof(GameEventBus).GetMethod("ResetCameraInput",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, null);
        }
    }

    [TestCase(-1)]
    [TestCase(2)]
    [TestCase(99)]
    public void InvalidMalletId_IsIgnoredAndDoesNotTouchRightMallet(int invalidMalletId)
    {
        Sample(0.4f, 0, malletId: 1);
        Sample(-0.05f, 33, malletId: invalidMalletId);
        Assert.That(hits, Is.Empty);

        Sample(0.4f, 10, malletId: invalidMalletId);
        Sample(-0.05f, 40, malletId: invalidMalletId);
        Assert.That(hits, Is.Empty);

        // Right mallet history from t=0 must remain intact for a valid t=33 sample.
        Sample(-0.05f, 33, malletId: 1);
        Assert.That(hits, Has.Count.EqualTo(1));
        Assert.That(hits[0].MalletId, Is.EqualTo(1));
    }

    [Test]
    public void ProcessMalletStates_IgnoresInvalidMalletIdWithoutBlockingValidMallet()
    {
        core.ProcessMalletStates(
            new MalletState { MalletId = 0, Position = LocalToWorld(0f, 0.4f), Confidence = 1f, TimestampMs = 0 },
            new MalletState { MalletId = 42, Position = LocalToWorld(0f, 0.4f), Confidence = 1f, TimestampMs = 0 });
        core.ProcessMalletStates(
            new MalletState { MalletId = 0, Position = LocalToWorld(0f, -0.05f), Confidence = 1f, TimestampMs = 33 },
            new MalletState { MalletId = 42, Position = LocalToWorld(0f, -0.05f), Confidence = 1f, TimestampMs = 33 });

        Assert.That(hits, Has.Count.EqualTo(1));
        Assert.That(hits[0].MalletId, Is.EqualTo(0));
    }

    [Test]
    public void NonFiniteInputs_AreRejectedWithoutCorruptingHistoryOrTimestamp()
    {
        Sample(0.4f, 10, malletId: 0);

        // NaN/Infinity positions, timestamps, velocities, and confidences must all be ignored cleanly.
        core.ProcessMalletState(new MalletState
        {
            MalletId = 0,
            Position = new Vector3(float.NaN, 0f, -0.05f),
            Confidence = 1f,
            TimestampMs = 20
        });
        core.ProcessMalletState(new MalletState
        {
            MalletId = 0,
            Position = LocalToWorld(0f, -0.05f),
            Velocity = new Vector3(float.PositiveInfinity, 0f, 0f),
            Confidence = 1f,
            TimestampMs = 25
        });
        core.ProcessMalletState(new MalletState
        {
            MalletId = 0,
            Position = LocalToWorld(0f, -0.05f),
            Confidence = float.NaN,
            TimestampMs = 30
        });
        core.ProcessMalletState(new MalletState
        {
            MalletId = 0,
            Position = LocalToWorld(0f, -0.05f),
            Confidence = 1f,
            TimestampMs = double.PositiveInfinity
        });
        core.ProcessMalletState(new MalletState
        {
            MalletId = 0,
            Position = LocalToWorld(0f, -0.05f),
            Confidence = 1f,
            TimestampMs = double.NaN
        });
        // Extremely tiny dt that underflows float seconds to 0 must not produce Inf/NaN hit or overwrite history.
        core.ProcessMalletState(new MalletState
        {
            MalletId = 0,
            Position = LocalToWorld(0f, -0.05f),
            Confidence = 1f,
            TimestampMs = 10.0 + 1e-40
        });

        Assert.That(hits, Is.Empty);

        // Valid sample after rejected samples still joins with t=10 sample and hits once.
        Sample(-0.05f, 43, malletId: 0);
        Assert.That(hits, Has.Count.EqualTo(1));
    }

    [Test]
    public void NonFiniteInputs_DoNotCorruptActiveStrikeLock()
    {
        Sample(0.4f, 0, malletId: 0);
        Sample(-0.05f, 33, malletId: 0);
        Assert.That(hits, Has.Count.EqualTo(1));

        // Corrupt samples while locked must not unlock or break timestamp ordering.
        core.ProcessMalletState(new MalletState
        {
            MalletId = 0,
            Position = new Vector3(0f, 0f, float.PositiveInfinity),
            Confidence = 1f,
            TimestampMs = 66
        });
        core.ProcessMalletState(new MalletState
        {
            MalletId = 0,
            Position = LocalToWorld(0f, 0.5f),
            Velocity = new Vector3(float.NaN, 0f, 0f),
            Confidence = 1f,
            TimestampMs = 70
        });

        // Still locked: moving across surface without retreating beyond RearmDistance must not hit.
        Sample(0.1f, 80, malletId: 0);
        Sample(-0.05f, 100, malletId: 0);
        Assert.That(hits, Has.Count.EqualTo(1));

        // Retreating beyond RearmDistance unlocks and allows the next strike.
        Sample(0.4f, 130, malletId: 0);
        Sample(-0.05f, 160, malletId: 0);
        Assert.That(hits, Has.Count.EqualTo(2));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CircularAndAnnularPads_HaveNonOverlappingInnerOuterBoundaries(bool registerOuterFirst)
    {
        core.ClearPads();
        var inner = new InstrumentPad
        {
            PadId = "inner",
            HitSurface = surface.transform,
            Shape = PadShape.Circle,
            OuterRadius = 0.1f
        };
        var outer = new InstrumentPad
        {
            PadId = "outer",
            HitSurface = surface.transform,
            Shape = PadShape.Annulus,
            InnerRadius = 0.1f,
            OuterRadius = 0.2f
        };

        if (registerOuterFirst)
        {
            core.RegisterPad(outer);
            core.RegisterPad(inner);
        }
        else
        {
            core.RegisterPad(inner);
            core.RegisterPad(outer);
        }

        // Inside inner circle (r = 0.05) -> inner only
        StrikeAtXY(0.05f, 0f, 0);
        Assert.That(hits, Has.Count.EqualTo(1));
        Assert.That(hits[0].InstrumentId, Is.EqualTo("inner"));

        // Exact shared boundary (r = 0.10) -> inner only (non-overlapping regardless of registration order)
        StrikeAtXY(0.1f, 0f, 200);
        Assert.That(hits, Has.Count.EqualTo(2));
        Assert.That(hits[1].InstrumentId, Is.EqualTo("inner"));

        // Inside outer annulus (r = 0.105) -> outer only
        StrikeAtXY(0.105f, 0f, 400);
        Assert.That(hits, Has.Count.EqualTo(3));
        Assert.That(hits[2].InstrumentId, Is.EqualTo("outer"));

        // Diagonal inside outer annulus (sqrt(0.12^2 + 0.12^2) ~= 0.1697 <= 0.2, outside square would fail if AABB was wrong)
        StrikeAtXY(0.12f, 0.12f, 600);
        Assert.That(hits, Has.Count.EqualTo(4));
        Assert.That(hits[3].InstrumentId, Is.EqualTo("outer"));

        // Corner of bounding square (0.18, 0.18 => r ~= 0.254 > 0.2) -> misses annulus
        StrikeAtXY(0.18f, 0.18f, 800);
        Assert.That(hits, Has.Count.EqualTo(4));
    }

    [Test]
    public void DynamicTransformScaleChanges_UpdateEffectiveHitAreaWithoutReRegistering()
    {
        core.ClearPads();
        core.RegisterPad(new InstrumentPad
        {
            PadId = "scaled_circle",
            HitSurface = surface.transform,
            Shape = PadShape.Circle,
            OuterRadius = 0.1f
        });

        // At scale 1, x=0.15 is outside OuterRadius=0.1.
        SampleWorld(new Vector3(0.15f, 0f, 0.4f), 0);
        SampleWorld(new Vector3(0.15f, 0f, -0.05f), 33);
        Assert.That(hits, Is.Empty);

        // Scaling surface to 2x expands effective world radius to 0.2, so x=0.15 now hits.
        surface.transform.localScale = new Vector3(2f, 2f, 1f);
        SampleWorld(new Vector3(0.15f, 0f, 0.4f), 100);
        SampleWorld(new Vector3(0.15f, 0f, -0.05f), 133);
        Assert.That(hits, Has.Count.EqualTo(1));

        // Scaling surface to 0.5x shrinks effective world radius to 0.05, so x=0.08 misses.
        surface.transform.localScale = new Vector3(0.5f, 0.5f, 1f);
        SampleWorld(new Vector3(0.08f, 0f, 0.4f), 200);
        SampleWorld(new Vector3(0.08f, 0f, -0.05f), 233);
        Assert.That(hits, Has.Count.EqualTo(1));

        // Degenerate zero scale safely suppresses hits instead of collapsing coordinates to (0,0).
        surface.transform.localScale = new Vector3(0f, 1f, 1f);
        SampleWorld(new Vector3(0.5f, 0f, 0.4f), 300);
        SampleWorld(new Vector3(0.5f, 0f, -0.05f), 333);
        Assert.That(hits, Has.Count.EqualTo(1));
    }

    [Test]
    public void TrackingLostAtEvent_ClearsOnlyTargetMalletHistoryAndLock()
    {
        core.Subscribe();

        // Mallet 0 hits and becomes locked; Mallet 1 has a pending pre-strike sample at z=0.4.
        Sample(0.4f, 0, malletId: 0);
        Sample(-0.05f, 33, malletId: 0);
        Assert.That(hits, Has.Count.EqualTo(1));
        Sample(0.4f, 33, malletId: 1);

        // Out-of-order tracking lost timestamp (< latestTimestampMs 33) must be ignored.
        GameEventBus.PublishTrackingLost(0, 10);

        // Mallet 0 is still locked, so crossing without full retreat does not fire.
        Sample(0.1f, 50, malletId: 0);
        Sample(-0.05f, 66, malletId: 0);
        Assert.That(hits, Has.Count.EqualTo(1));

        // Ordered tracking lost for mallet 0 at t=70 unlocks mallet 0 and clears its history, leaving mallet 1 intact.
        GameEventBus.PublishTrackingLost(0, 70);

        // Mallet 1 was unaffected and completes its strike cleanly.
        Sample(-0.05f, 75, malletId: 1);
        Assert.That(hits, Has.Count.EqualTo(2));
        Assert.That(hits[1].MalletId, Is.EqualTo(1));

        // First sample for mallet 0 after recovery does not falsely bridge across tracking loss,
        // and because lock was cleared, the next full strike succeeds without retreating first.
        Sample(-0.05f, 80, malletId: 0);
        Assert.That(hits, Has.Count.EqualTo(2));

        Sample(0.4f, 110, malletId: 0);
        Sample(-0.05f, 140, malletId: 0);
        Assert.That(hits, Has.Count.EqualTo(3));
        Assert.That(hits[2].MalletId, Is.EqualTo(0));
    }

    [Test]
    public void ResetTrackingStateAndContextDeactivation_ClearHistoryAndLocks()
    {
        try
        {
            // Lock mallet 0 and stage mallet 1 pre-strike sample.
            Sample(0.4f, 0, malletId: 0);
            Sample(-0.05f, 33, malletId: 0);
            Sample(0.4f, 33, malletId: 1);
            Assert.That(hits, Has.Count.EqualTo(1));

            // Session stop / state reset clears both mallets while keeping registered pads.
            core.ResetTrackingState();

            // First sample on mallet 1 must not join with pre-reset sample.
            Sample(-0.05f, 50, malletId: 1);
            Assert.That(hits, Has.Count.EqualTo(1));

            // Mallet 0 lock was cleared, so a new strike succeeds immediately.
            Sample(0.4f, 80, malletId: 0);
            Sample(-0.05f, 110, malletId: 0);
            Assert.That(hits, Has.Count.EqualTo(2));

            // Context deactivation (BeginContext without ActivateContext) blocks processing.
            GameEventBus.BeginContext(2);
            Sample(0.4f, 140, malletId: 1, version: 2);
            Sample(-0.05f, 170, malletId: 1, version: 2);
            Assert.That(hits, Has.Count.EqualTo(2));
        }
        finally
        {
            typeof(GameEventBus).GetMethod("ResetCameraInput",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, null);
        }
    }

    private void StrikeAtXY(float x, float y, double startMs, int malletId = 0)
    {
        core.ProcessMalletState(new MalletState
        {
            MalletId = malletId,
            Position = surface.transform.TransformPoint(new Vector3(x, y, 0.4f)),
            Confidence = 1f,
            TimestampMs = startMs
        });
        core.ProcessMalletState(new MalletState
        {
            MalletId = malletId,
            Position = surface.transform.TransformPoint(new Vector3(x, y, -0.05f)),
            Confidence = 1f,
            TimestampMs = startMs + 33.0
        });
    }

    private void SampleWorld(Vector3 worldPosition, double timestampMs, int malletId = 0)
    {
        core.ProcessMalletState(new MalletState
        {
            MalletId = malletId,
            Position = worldPosition,
            Confidence = 1f,
            TimestampMs = timestampMs
        });
    }

    private Vector3 LocalToWorld(float x, float z) => surface.transform.TransformPoint(new Vector3(x, 0f, z));

    private void Sample(float z, double timestampMs, float x = 0f, int malletId = 0, float confidence = 1f, long version = 0)
    {
        core.ProcessMalletState(new MalletState
        {
            ContextVersion = version,
            MalletId = malletId,
            Position = surface.transform.TransformPoint(new Vector3(x, 0f, z)),
            Confidence = confidence,
            TimestampMs = timestampMs
        });
    }
}
