using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

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

    [UnityTest]
    public IEnumerator MainScene_CameraSwitch_RecoversSessionAndControlBarAndNextInputRequestsStop()
    {
        ResetBus();
        EditorSceneManager.OpenScene("Assets/Shared/Scenes/Main.unity");
        yield return new EnterPlayMode();
        yield return null;

        try
        {
            AssertCameraSwitchRecoversSession();
        }
        finally
        {
            GameEventBus.PublishSessionRunningChanged(false);
            GameEventBus.PublishCameraDeviceSelected(string.Empty);
        }

        yield return new ExitPlayMode();
        ResetBus();
    }

    [UnityTest]
    public IEnumerator CalibrationManager_DeviceSwitchMidCalibration_DiscardsPriorSamplesAndMatchesControl()
    {
        ResetBus();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        yield return null;
        var host = new GameObject("CalibrationHost");
        var manager = host.AddComponent<CalibrationManager>();
        yield return null;

        try
        {
            AssertDeviceSwitchDiscardsSamples(manager);
        }
        finally
        {
            GameEventBus.PublishCameraDeviceSelected(string.Empty);
            Object.Destroy(host);
        }

        yield return new ExitPlayMode();
        ResetBus();
    }

    [UnityTest]
    public IEnumerator MainScene_CameraErrorDuringCalibration_DisplaysErrorInHudBannerWithoutCancelling()
    {
        ResetBus();
        EditorSceneManager.OpenScene("Assets/Shared/Scenes/Main.unity");
        yield return new EnterPlayMode();
        yield return null;

        try
        {
            AssertCameraErrorShownInHudDuringCalibration();
        }
        finally
        {
            GameEventBus.PublishSessionRunningChanged(false);
            GameEventBus.PublishCameraDeviceSelected(string.Empty);
        }

        yield return new ExitPlayMode();
        ResetBus();
    }

    private static void AssertCameraSwitchRecoversSession()
    {
        var bar = Object.FindFirstObjectByType<ScreenControlBar>();
        Assert.That(bar, Is.Not.Null);
        Assert.That(GameObject.Find("TrackingSystem"), Is.Not.Null);

        Transform runTransform = bar.transform.Find("ScreenControls/SafeArea/BottomBar/RunStop");
        Assert.That(runTransform, Is.Not.Null);
        Button runButton = runTransform.GetComponent<Button>();
        Text runLabel = runTransform.GetComponentInChildren<Text>();
        Assert.That(runButton, Is.Not.Null);
        Assert.That(runLabel, Is.Not.Null);

        double timeMs = 1000.0;
        Guid sessionA = Guid.NewGuid();
        PublishCameraState(sessionA, "Camera A", CameraInputState.Waiting, ref timeMs);
        PublishCameraState(sessionA, "Camera A", CameraInputState.Streaming, ref timeMs);

        Assert.That(GameEventBus.IsSessionRunning, Is.True);
        Assert.That(bar.State.IsRunning, Is.True);
        Assert.That(runLabel.text, Is.EqualTo("정지"));

        GameEventBus.PublishCameraDeviceSelected("Camera B");
        PublishCameraState(sessionA, "Camera A", CameraInputState.Stopped, ref timeMs);
        Assert.That(GameEventBus.IsSessionRunning, Is.False);
        Assert.That(bar.State.IsRunning, Is.False);
        Assert.That(runLabel.text, Is.EqualTo("실행"));

        Guid sessionB = Guid.NewGuid();
        PublishCameraState(sessionB, "Camera B", CameraInputState.Waiting, ref timeMs);
        Assert.That(GameEventBus.IsSessionRunning, Is.False);
        Assert.That(bar.State.IsRunning, Is.False);

        PublishCameraState(sessionB, "Camera B", CameraInputState.Streaming, ref timeMs);
        Assert.That(GameEventBus.IsSessionRunning, Is.True);
        Assert.That(bar.State.IsRunning, Is.True);
        Assert.That(bar.State.IsPending, Is.False);
        Assert.That(runLabel.text, Is.EqualTo("정지"));

        // Stale Stopped event from old session A must be ignored after session B started.
        PublishCameraState(sessionA, "Camera A", CameraInputState.Stopped, ref timeMs);
        Assert.That(GameEventBus.IsSessionRunning, Is.True);
        Assert.That(bar.State.IsRunning, Is.True);

        var requests = new List<RunSessionRequest>();
        void CaptureRequest(RunSessionRequest req) => requests.Add(req);
        GameEventBus.OnRunSessionRequested += CaptureRequest;
        try
        {
            runButton.onClick.Invoke();
            Assert.That(requests, Has.Count.EqualTo(1));
            Assert.That(requests[0].Run, Is.False, "Next button press after recovery must request Stop.");
        }
        finally
        {
            GameEventBus.OnRunSessionRequested -= CaptureRequest;
        }
    }

    private static void AssertDeviceSwitchDiscardsSamples(CalibrationManager manager)
    {
        bool completed = false;
        Vector2 pMin = default;
        Vector2 pMax = default;
        double aMin = 0.0;
        double aMax = 0.0;

        void OnDone(Vector2 minP, Vector2 maxP, double minA, double maxA)
        {
            completed = true;
            pMin = minP;
            pMax = maxP;
            aMin = minA;
            aMax = maxA;
        }

        GameEventBus.OnCalibrationCompleted += OnDone;
        try
        {
            // B-only control calibration.
            double timeMs = 2000.0;
            Guid controlSessionB = Guid.NewGuid();
            PublishCameraState(controlSessionB, "Camera B", CameraInputState.Waiting, ref timeMs);
            PublishCameraState(controlSessionB, "Camera B", CameraInputState.Streaming, ref timeMs);

            manager.Begin();
            FeedBControlCalibrationSteps();
            Assert.That(completed, Is.True);
            Vector2 controlMinPixel = pMin;
            Vector2 controlMaxPixel = pMax;
            double controlMinArea = aMin;
            double controlMaxArea = aMax;
            Assert.That(controlMinPixel, Is.EqualTo(new Vector2(300f, 240f)));

            // Scenario A: switch camera via CameraInputStateData mid-step 1 (30/60 samples).
            completed = false;
            Guid sessionA = Guid.NewGuid();
            PublishCameraState(sessionA, "Camera A", CameraInputState.Waiting, ref timeMs);
            PublishCameraState(sessionA, "Camera A", CameraInputState.Streaming, ref timeMs);

            manager.Begin();
            FeedSamples(30, new Vector2(100f, 80f), 1200u);
            Assert.That(manager.CurrentStep, Is.EqualTo(CalibrationStep.TopLeft));
            Assert.That(manager.CurrentSampleCount, Is.EqualTo(30));

            PublishCameraState(sessionA, "Camera A", CameraInputState.Stopped, ref timeMs);
            Guid sessionB = Guid.NewGuid();
            PublishCameraState(sessionB, "Camera B", CameraInputState.Waiting, ref timeMs);
            PublishCameraState(sessionB, "Camera B", CameraInputState.Streaming, ref timeMs);

            Assert.That(manager.IsCalibrating, Is.False);
            Assert.That(manager.CurrentStep, Is.EqualTo(CalibrationStep.NotStarted));
            Assert.That(manager.CurrentSampleCount, Is.Zero);

            FeedSamples(30, new Vector2(300f, 240f), 1200u);
            Assert.That(manager.CurrentStep, Is.EqualTo(CalibrationStep.NotStarted));
            Assert.That(manager.CurrentSampleCount, Is.Zero);
            Assert.That(completed, Is.False);

            manager.Begin();
            FeedBControlCalibrationSteps();
            Assert.That(completed, Is.True);
            Assert.That(pMin, Is.EqualTo(controlMinPixel));
            Assert.That(pMax, Is.EqualTo(controlMaxPixel));
            Assert.That(aMin, Is.EqualTo(controlMinArea).Within(0.01));
            Assert.That(aMax, Is.EqualTo(controlMaxArea).Within(0.01));

            // Scenario B: switch camera via PublishCameraDeviceSelected during step 2 (TopRight).
            completed = false;
            Guid sessionA2 = Guid.NewGuid();
            PublishCameraState(sessionA2, "Camera A", CameraInputState.Waiting, ref timeMs);
            PublishCameraState(sessionA2, "Camera A", CameraInputState.Streaming, ref timeMs);

            manager.Begin();
            FeedStepSamples(new Vector2(100f, 80f), 1200u);
            Assert.That(manager.CurrentStep, Is.EqualTo(CalibrationStep.TopRight));
            FeedSamples(15, new Vector2(900f, 90f), 1100u);
            Assert.That(manager.CurrentSampleCount, Is.EqualTo(15));

            GameEventBus.PublishCameraDeviceSelected("Camera B");
            Assert.That(manager.IsCalibrating, Is.False);
            Assert.That(manager.CurrentStep, Is.EqualTo(CalibrationStep.NotStarted));
            Assert.That(manager.CurrentSampleCount, Is.Zero);

            manager.Begin();
            FeedBControlCalibrationSteps();
            Assert.That(completed, Is.True);
            Assert.That(pMin, Is.EqualTo(controlMinPixel));
            Assert.That(pMax, Is.EqualTo(controlMaxPixel));
            Assert.That(aMin, Is.EqualTo(controlMinArea).Within(0.01));
            Assert.That(aMax, Is.EqualTo(controlMaxArea).Within(0.01));
        }
        finally
        {
            GameEventBus.OnCalibrationCompleted -= OnDone;
        }
    }

    private static void AssertCameraErrorShownInHudDuringCalibration()
    {
        var calibrationManager = Object.FindFirstObjectByType<CalibrationManager>();
        var hud = Object.FindFirstObjectByType<TrackingStatusHud>();
        Assert.That(calibrationManager, Is.Not.Null);
        Assert.That(hud, Is.Not.Null);

        Transform bannerTransform = hud.transform.Find("TrackingStatusCanvas/SafeArea/GuidanceBanner");
        Assert.That(bannerTransform, Is.Not.Null);
        GameObject bannerObj = bannerTransform.gameObject;
        Text bannerText = bannerTransform.Find("Label")?.GetComponent<Text>();
        Assert.That(bannerText, Is.Not.Null);

        double timeMs = 3000.0;
        Guid sessionA = Guid.NewGuid();
        PublishCameraState(sessionA, "Camera A", CameraInputState.Waiting, ref timeMs);
        PublishCameraState(sessionA, "Camera A", CameraInputState.Streaming, ref timeMs);

        calibrationManager.Begin();
        FeedSamples(12, new Vector2(150f, 150f), 1000u);
        Assert.That(calibrationManager.IsCalibrating, Is.True);
        Assert.That(calibrationManager.CurrentStep, Is.EqualTo(CalibrationStep.TopLeft));
        Assert.That(calibrationManager.CurrentSampleCount, Is.EqualTo(12));
        Assert.That(bannerObj.activeSelf, Is.True);
        Assert.That(bannerText.text, Does.Contain("(12/60)"));
        Assert.That(bannerText.text, Does.Contain("모서리에 유지"));

        PublishCameraState(
            sessionA,
            "Camera A",
            CameraInputState.Error,
            ref timeMs,
            CameraInputError.FrameTimeout,
            "Camera frame stream interrupted.");

        Assert.That(calibrationManager.IsCalibrating, Is.True);
        Assert.That(calibrationManager.CurrentStep, Is.EqualTo(CalibrationStep.TopLeft));
        Assert.That(calibrationManager.CurrentSampleCount, Is.EqualTo(12));
        Assert.That(bannerObj.activeSelf, Is.True);
        Assert.That(hud.StatusBannerText, Does.Contain("카메라 오류"));
        Assert.That(hud.StatusBannerText, Does.Contain("Camera frame stream interrupted."));
        Assert.That(hud.StatusBannerText, Does.Not.Contain("모서리에 유지"));
        Assert.That(bannerText.text, Does.Contain("카메라 오류"));
        Assert.That(bannerText.text, Does.Contain("Camera frame stream interrupted."));
        Assert.That(bannerText.text, Does.Not.Contain("모서리에 유지"));

        // Same-device restart clears the error and restores calibration progress guidance.
        Guid sessionA2 = Guid.NewGuid();
        PublishCameraState(sessionA2, "Camera A", CameraInputState.Waiting, ref timeMs);
        PublishCameraState(sessionA2, "Camera A", CameraInputState.Streaming, ref timeMs);
        Assert.That(calibrationManager.CurrentStep, Is.EqualTo(CalibrationStep.TopLeft));
        Assert.That(calibrationManager.CurrentSampleCount, Is.EqualTo(12));
        Assert.That(hud.StatusBannerText, Does.Contain("(12/60)"));
        Assert.That(bannerText.text, Does.Contain("(12/60)"));

        // FrameTimeout with empty Description still displays a Korean camera error without cancelling calibration.
        PublishCameraState(
            sessionA2,
            "Camera A",
            CameraInputState.Error,
            ref timeMs,
            CameraInputError.FrameTimeout,
            string.Empty);
        Assert.That(calibrationManager.IsCalibrating, Is.True);
        Assert.That(bannerObj.activeSelf, Is.True);
        Assert.That(bannerText.text, Does.Contain("카메라 오류"));
        Assert.That(bannerText.text, Does.Contain("프레임"));
    }

    private static void FeedBControlCalibrationSteps()
    {
        FeedStepSamples(new Vector2(300f, 240f), 1200u);
        FeedStepSamples(new Vector2(1100f, 260f), 1100u);
        FeedStepSamples(new Vector2(320f, 600f), 1300u);
        FeedStepSamples(new Vector2(1080f, 620f), 1250u);
        FeedStepSamples(new Vector2(700f, 420f), 400u);
    }

    private static void PublishCameraState(
        Guid sessionId,
        string deviceName,
        CameraInputState state,
        ref double changedAtMs,
        CameraInputError error = CameraInputError.None,
        string description = null)
    {
        changedAtMs += 10.0;
        GameEventBus.PublishCameraInputState(new CameraInputStateData
        {
            CameraId = "primary",
            SessionId = sessionId,
            DeviceName = deviceName,
            State = state,
            ChangedAtMs = changedAtMs,
            Error = error,
            Description = description
        });
    }

    private static void ResetBus() => typeof(GameEventBus).GetMethod(
        "ResetCameraInput",
        BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);

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
