using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class GpuMalletTrackerTests
{
    private GameObject trackerObject;
    private GpuMalletTracker tracker;

    [SetUp]
    public void SetUp()
    {
        ResetEventBus();
        trackerObject = new GameObject("TrackerTestObject");
        trackerObject.SetActive(false);
        tracker = trackerObject.AddComponent<GpuMalletTracker>();
        tracker.minPixelArea = 100;
        tracker.pixelMin = new Vector2(0f, 0f);
        tracker.pixelMax = new Vector2(1280f, 720f);
        tracker.areaMin = 200.0;
        tracker.areaMax = 2000.0;
        tracker.worldMin = new Vector3(-5f, -3f, 0f);
        tracker.worldMax = new Vector3(5f, 3f, 2f);
        tracker.ConfigureMapperForTesting(1280f, 720f, frontFacing: true);
    }

    [TearDown]
    public void TearDown()
    {
        if (trackerObject != null)
        {
            UnityEngine.Object.DestroyImmediate(trackerObject);
        }
        ResetEventBus();
    }

    private static void ResetEventBus()
    {
        typeof(GameEventBus).GetMethod("ResetCameraInput",
            BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
    }

    [Test]
    public void InvalidOrOutOfRangeDepthPublishesTrackingLostInsteadOfClamping()
    {
        int lostAtCount = 0;
        int lostLegacyCount = 0;
        int lastLostId = -1;
        double lastLostTimestamp = -1.0;

        GameEventBus.OnTrackingLostAt += (id, ts) =>
        {
            lostAtCount++;
            lastLostId = id;
            lastLostTimestamp = ts;
        };
        GameEventBus.OnTrackingLost += _ => lostLegacyCount++;

        // Above minPixelArea (100), but below calibrated areaMin (200): must be rejected as tracking lost.
        MalletState tooSmall = tracker.ProcessLeftSampleForTesting(640u * 150u, 360u * 150u, 150u, 100.0);
        Assert.That(tooSmall.Confidence, Is.EqualTo(0.0f));
        Assert.That(tooSmall.Position, Is.EqualTo(Vector3.zero));
        Assert.That(tooSmall.Velocity, Is.EqualTo(Vector3.zero));
        Assert.That(lostAtCount, Is.EqualTo(1));
        Assert.That(lostLegacyCount, Is.EqualTo(1));
        Assert.That(lastLostId, Is.EqualTo(0));
        Assert.That(lastLostTimestamp, Is.EqualTo(100.0));

        // Above calibrated areaMax (2000): must also be rejected rather than clamped to worldMax.z.
        MalletState tooLarge = tracker.ProcessLeftSampleForTesting(640u * 2500u, 360u * 2500u, 2500u, 120.0);
        Assert.That(tooLarge.Confidence, Is.EqualTo(0.0f));
        Assert.That(tooLarge.Position, Is.EqualTo(Vector3.zero));
        Assert.That(lostAtCount, Is.EqualTo(2));
        Assert.That(lastLostTimestamp, Is.EqualTo(120.0));
    }

    [Test]
    public void ValidDepthSamplesProduceContinuousConfidenceAndNonZeroZVelocity()
    {
        int lostCount = 0;
        int rawSampleCount = 0;
        GameEventBus.OnTrackingLostAt += (_, __) => lostCount++;
        GameEventBus.OnRawMalletSample += _ => rawSampleCount++;

        uint area1 = 500u;
        MalletState s1 = tracker.ProcessLeftSampleForTesting(640u * area1, 360u * area1, area1, 100.0);
        Assert.That(s1.Confidence, Is.GreaterThan(0.0f).And.LessThan(1.0f));
        Assert.That(lostCount, Is.EqualTo(0));
        Assert.That(rawSampleCount, Is.EqualTo(1));

        uint area2 = 1400u;
        MalletState s2 = tracker.ProcessLeftSampleForTesting(640u * area2, 360u * area2, area2, 150.0);
        Assert.That(s2.Confidence, Is.GreaterThan(s1.Confidence).And.LessThanOrEqualTo(1.0f));
        Assert.That(s2.Velocity.z, Is.GreaterThan(0.0f));
        Assert.That(s2.Position.z, Is.GreaterThan(s1.Position.z));

        uint areaMaxSample = 2000u;
        MalletState s3 = tracker.ProcessLeftSampleForTesting(640u * areaMaxSample, 360u * areaMaxSample, areaMaxSample, 200.0);
        Assert.That(s3.Confidence, Is.EqualTo(1.0f).Within(0.0001f));
        Assert.That(lostCount, Is.EqualTo(0));
    }

    [Test]
    public void ReleaseResourcesClearsPendingReadbackFlagsCleanly()
    {
        tracker.MarkReadbackPendingForTesting(0);
        tracker.MarkReadbackPendingForTesting(2);
        Assert.That(tracker.HasAnyPendingReadbackForTesting(), Is.True);

        tracker.ReleaseResourcesForTesting();
        Assert.That(tracker.HasAnyPendingReadbackForTesting(), Is.False);
    }

    [Test]
    public void MissingShaderPublishesStartFailedAndDisablesTracker()
    {
        tracker.cameraId = "primary";
        tracker.trackerShader = null;
        UnityEngine.TestTools.LogAssert.Expect(LogType.Error, "GpuMalletTracker requires a compute shader.");

        // MonoBehaviour.OnEnable is not automatically invoked in EditMode on SetActive(true).
        trackerObject.SetActive(true);
        typeof(GpuMalletTracker).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(tracker, null);

        Assert.That(tracker.enabled, Is.False);
        Assert.That(GameEventBus.TryGetCameraInputState("primary", out CameraInputStateData state), Is.True);
        Assert.That(state.State, Is.EqualTo(CameraInputState.Error));
        Assert.That(state.Error, Is.EqualTo(CameraInputError.StartFailed));
    }
}
