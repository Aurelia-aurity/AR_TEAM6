using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class CalibrationAndStatusHudTests
{
    [UnityTest]
    public IEnumerator CalibrationManager_CollectsFiveStepsAndPublishesCompletedBounds()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        yield return null;
        var host = new GameObject("CalibrationHost");
        var manager = host.AddComponent<CalibrationManager>();
        yield return null;

        try
        {
            AssertCalibrationFlow(manager);
        }
        finally
        {
            Object.Destroy(host);
        }

        yield return new ExitPlayMode();
    }

    private static void AssertCalibrationFlow(CalibrationManager manager)
    {
        var progressSteps = new List<CalibrationStep>();
        manager.ProgressChanged += (step, _, __) => progressSteps.Add(step);

        bool completedFired = false;
        Vector2 pMin = default;
        Vector2 pMax = default;
        double aMin = 0.0;
        double aMax = 0.0;
        void OnDone(Vector2 minP, Vector2 maxP, double minA, double maxA)
        {
            completedFired = true;
            pMin = minP;
            pMax = maxP;
            aMin = minA;
            aMax = maxA;
        }

        GameEventBus.OnCalibrationCompleted += OnDone;
        try
        {
            manager.Begin();
            Assert.That(manager.IsCalibrating, Is.True);
            Assert.That(manager.CurrentStep, Is.EqualTo(CalibrationStep.TopLeft));

            // Right mallet samples (MalletId=1) must be ignored during calibration.
            GameEventBus.OnRawMalletSample?.Invoke(new RawMalletSample
            {
                MalletId = 1,
                PixelPosition = new Vector2(999f, 999f),
                PixelArea = 9999u,
                TimestampMs = 1.0
            });
            Assert.That(manager.CurrentSampleCount, Is.Zero);

            FeedStepSamples(new Vector2(100f, 100f), 1200u);
            Assert.That(manager.CurrentStep, Is.EqualTo(CalibrationStep.TopRight));
            FeedStepSamples(new Vector2(1100f, 120f), 1100u);
            Assert.That(manager.CurrentStep, Is.EqualTo(CalibrationStep.BottomLeft));
            FeedStepSamples(new Vector2(120f, 600f), 1300u);
            Assert.That(manager.CurrentStep, Is.EqualTo(CalibrationStep.BottomRight));
            FeedStepSamples(new Vector2(1080f, 620f), 1250u);
            Assert.That(manager.CurrentStep, Is.EqualTo(CalibrationStep.RestOnDesk));
            Assert.That(completedFired, Is.False);

            FeedStepSamples(new Vector2(600f, 360f), 400u);
            Assert.That(manager.CurrentStep, Is.EqualTo(CalibrationStep.Completed));
            Assert.That(manager.IsCalibrating, Is.False);
            Assert.That(completedFired, Is.True);
            Assert.That(pMin, Is.EqualTo(new Vector2(100f, 100f)));
            Assert.That(pMax, Is.EqualTo(new Vector2(1100f, 620f)));
            Assert.That(aMin, Is.EqualTo(400.0).Within(0.01));
            Assert.That(aMax, Is.EqualTo(1300.0).Within(0.01));
            Assert.That(progressSteps, Contains.Item(CalibrationStep.Completed));

            // Canceling mid-calibration resets without firing completion.
            completedFired = false;
            manager.Begin();
            FeedSamples(5, new Vector2(50f, 50f), 500u);
            Assert.That(manager.CurrentSampleCount, Is.EqualTo(5));
            manager.Cancel();
            Assert.That(manager.CurrentStep, Is.EqualTo(CalibrationStep.NotStarted));
            Assert.That(manager.CurrentSampleCount, Is.Zero);
            Assert.That(manager.IsCalibrating, Is.False);
            Assert.That(completedFired, Is.False);
        }
        finally
        {
            GameEventBus.OnCalibrationCompleted -= OnDone;
        }
    }

    [UnityTest]
    public IEnumerator TrackingStatusHud_DistinguishesIndependentMalletStatesAndRecovers()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        yield return null;
        var host = new GameObject("HudHost");
        var hud = host.AddComponent<TrackingStatusHud>();
        yield return null;

        try
        {
            GameEventBus.PublishSessionRunningChanged(true);

            double t = Time.realtimeSinceStartupAsDouble * 1000.0 + 10.0;
            long v = GameEventBus.ContextVersion;
            GameEventBus.OnMalletsTracked?.Invoke(
                new MalletState { MalletId = 0, Position = Vector3.forward, Confidence = 1f, TimestampMs = t, ContextVersion = v },
                new MalletState { MalletId = 1, Position = Vector3.forward, Confidence = 1f, TimestampMs = t, ContextVersion = v });

            Assert.That(hud.IsLeftTracked, Is.True);
            Assert.That(hud.IsRightTracked, Is.True);
            Assert.That(hud.StatusBannerText, Is.Empty);

            GameEventBus.PublishTrackingLost(1, t + 10.0);
            Assert.That(hud.IsLeftTracked, Is.True, "Losing right mallet must keep left mallet tracked.");
            Assert.That(hud.IsRightTracked, Is.False);
            Assert.That(hud.StatusBannerText, Does.Contain("오른채"));

            GameEventBus.OnMalletsTracked?.Invoke(
                new MalletState { MalletId = 0, Position = Vector3.forward, Confidence = 1f, TimestampMs = t + 20.0, ContextVersion = v },
                new MalletState { MalletId = 1, Position = Vector3.forward, Confidence = 1f, TimestampMs = t + 20.0, ContextVersion = v });
            Assert.That(hud.IsLeftTracked, Is.True);
            Assert.That(hud.IsRightTracked, Is.True);
            Assert.That(hud.StatusBannerText, Is.Empty);
        }
        finally
        {
            GameEventBus.PublishSessionRunningChanged(false);
            Object.Destroy(host);
        }

        yield return new ExitPlayMode();
    }

    private static void FeedStepSamples(Vector2 pixel, uint area) =>
        FeedSamples(CalibrationManager.SamplesPerStep, pixel, area);

    private static void FeedSamples(int count, Vector2 pixel, uint area)
    {
        for (int i = 0; i < count; i++)
        {
            GameEventBus.OnRawMalletSample?.Invoke(new RawMalletSample
            {
                MalletId = 0,
                PixelPosition = pixel,
                PixelArea = area,
                TimestampMs = i + 1
            });
        }
    }
}
