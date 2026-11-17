using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class CameraInputContractTests
{
    [SetUp]
    public void SetUp() => Reset();

    [TearDown]
    public void TearDown() => Reset();

    private static void Reset()
    {
        typeof(GameEventBus).GetMethod("ResetCameraInput",
            BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
    }

    private static CameraInputStateData State(string id, Guid session, CameraInputState state)
    {
        return new CameraInputStateData
        {
            CameraId = id, SessionId = session, DeviceName = "Phone camera",
            State = state, ChangedAtMs = 100,
            Error = state == CameraInputState.Error ? CameraInputError.FrameTimeout : CameraInputError.None
        };
    }

    [Test]
    public void LateSubscriberCanQueryErrorWithoutWaitingForAnotherStateChange()
    {
        var expected = State("primary", Guid.NewGuid(), CameraInputState.Error);
        GameEventBus.PublishCameraInputState(expected);
        int notifications = 0;
        GameEventBus.OnCameraInputStateChanged += _ => notifications++;

        Assert.That(GameEventBus.TryGetCameraInputState("primary", out var actual), Is.True);
        Assert.That(actual.SessionId, Is.EqualTo(expected.SessionId));
        Assert.That(actual.State, Is.EqualTo(CameraInputState.Error));
        Assert.That(actual.Error, Is.EqualTo(CameraInputError.FrameTimeout));
        Assert.That(notifications, Is.Zero, "Querying must not broadcast a replay to other consumers.");
    }

    [Test]
    public void RestartUpdatesOnlyItsCameraAndCacheIsCurrentInsideNotification()
    {
        var oldSession = Guid.NewGuid();
        var newSession = Guid.NewGuid();
        GameEventBus.PublishCameraInputState(State("primary", oldSession, CameraInputState.Stopped));
        GameEventBus.PublishCameraInputState(State("secondary", oldSession, CameraInputState.Streaming));
        int notifications = 0;
        GameEventBus.OnCameraInputStateChanged += state =>
        {
            notifications++;
            Assert.That(GameEventBus.TryGetCameraInputState(state.CameraId, out var cached), Is.True);
            Assert.That(cached.SessionId, Is.EqualTo(newSession));
            Assert.That(cached.State, Is.EqualTo(CameraInputState.Waiting));
        };
        GameEventBus.PublishCameraInputState(State("primary", newSession, CameraInputState.Waiting));

        Assert.That(notifications, Is.EqualTo(1));
        Assert.That(GameEventBus.TryGetCameraInputState("secondary", out var secondary), Is.True);
        Assert.That(secondary.SessionId, Is.EqualTo(oldSession));
        Assert.That(secondary.State, Is.EqualTo(CameraInputState.Streaming));
        Assert.That(GameEventBus.TryGetCameraInputState("unknown", out _), Is.False);
    }

    [Test]
    public void EmptyCameraIdIsIgnoredForPublishingAndLookup()
    {
        int notifications = 0;
        GameEventBus.OnCameraInputStateChanged += _ => notifications++;

        GameEventBus.PublishCameraInputState(State(string.Empty, Guid.NewGuid(), CameraInputState.Waiting));

        Assert.That(notifications, Is.Zero);
        Assert.That(GameEventBus.TryGetCameraInputState(string.Empty, out _), Is.False);
        Assert.That(GameEventBus.TryGetCameraInputState(null, out _), Is.False);
    }

    [Test]
    public void FramesAreSynchronousBorrowedReferencesAndAreNotReplayed()
    {
        var texture = new Texture2D(2, 2);
        try
        {
            var frame = new CameraFrameData
            {
                CameraId = "primary", SessionId = Guid.NewGuid(), Texture = texture,
                ClockwiseRotation = 90, VerticallyMirrored = true, ReceivedAtMs = 123
            };
            CameraFrameData received = default;
            Action<CameraFrameData> handler = value => received = value;
            GameEventBus.OnCameraFrameReceived += handler;
            GameEventBus.PublishCameraFrame(frame);
            Assert.That(received.Texture, Is.SameAs(texture));
            Assert.That(received.SessionId, Is.EqualTo(frame.SessionId));
            Assert.That(received.ClockwiseRotation, Is.EqualTo(90));
            Assert.That(received.VerticallyMirrored, Is.True);
            Assert.That(received.ReceivedAtMs, Is.EqualTo(123));
            GameEventBus.OnCameraFrameReceived -= handler;

            int lateFrames = 0;
            GameEventBus.OnCameraFrameReceived += _ => lateFrames++;
            GameEventBus.PublishCameraInputState(State("primary", frame.SessionId, CameraInputState.Stopped));
            Assert.That(lateFrames, Is.Zero);
            Assert.That(texture != null, Is.True, "The bus must not destroy the provider's texture.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    [Test]
    public void PlaySessionResetClearsCachedStateAndCameraSubscribers()
    {
        int notifications = 0;
        GameEventBus.OnCameraInputStateChanged += _ => notifications++;
        GameEventBus.OnCameraFrameReceived += _ => notifications++;
        GameEventBus.OnMalletsTracked += (_, __) => notifications++;
        GameEventBus.OnTrackingLost += _ => notifications++;
        GameEventBus.OnTrackingLostAt += (_, __) => notifications++;
        GameEventBus.OnRawMalletSample += _ => notifications++;
        GameEventBus.OnCalibrationCompleted += (_, __, ___, ____) => notifications++;
        GameEventBus.OnValidHit += _ => notifications++;
        GameEventBus.PublishCameraInputState(State("primary", Guid.NewGuid(), CameraInputState.Error));
        Reset();
        Assert.That(GameEventBus.TryGetCameraInputState("primary", out _), Is.False);
        GameEventBus.PublishCameraInputState(State("primary", Guid.NewGuid(), CameraInputState.Waiting));
        GameEventBus.PublishCameraFrame(default);
        GameEventBus.OnMalletsTracked?.Invoke(default, default);
        GameEventBus.OnTrackingLost?.Invoke(0);
        GameEventBus.OnTrackingLostAt?.Invoke(0, 0);
        GameEventBus.OnRawMalletSample?.Invoke(default);
        GameEventBus.OnCalibrationCompleted?.Invoke(default, default, 0, 0);
        GameEventBus.OnValidHit?.Invoke(default);
        Assert.That(notifications, Is.EqualTo(1));
    }
}
