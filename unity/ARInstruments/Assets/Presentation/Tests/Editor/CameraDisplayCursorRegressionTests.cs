using System;
using System.Collections;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class CameraDisplayCursorRegressionTests
{
    [UnityTest]
    public IEnumerator SameFrameWaitingOrErrorRestartDiscardsPriorTrackingImmediately()
    {
        EditorSceneManager.OpenScene("Assets/Presentation/Scenes/CameraDisplayPreview.unity");
        yield return new EnterPlayMode();
        yield return null;
        yield return null;
        Object.FindFirstObjectByType<MalletCursorPreviewSource>().enabled = false;
        Object.FindFirstObjectByType<CameraDisplayPreviewSource>().enabled = false;
        CameraDisplayView display = Object.FindFirstObjectByType<CameraDisplayView>();
        MalletCursorPresenter cursors = Object.FindFirstObjectByType<MalletCursorPresenter>();
        Texture2D texture = new Texture2D(320, 180);
        display.PresentFrame(texture);
        cursors.SetDebugCursorsEnabled(true);
        RectTransform left = FindCursor(display, "LeftCursor");
        RectTransform right = FindCursor(display, "RightCursor");

        foreach (bool useError in new[] { false, true })
        {
            MalletState previousLeft = State(0);
            MalletState previousRight = State(1);
            GameEventBus.OnMalletsTracked?.Invoke(previousLeft, previousRight);
            yield return null;
            yield return null;
            Assert.That(left.gameObject.activeInHierarchy, Is.True);
            Assert.That(right.gameObject.activeInHierarchy, Is.True);

            if (useError) display.ShowError("Restarting after an input error");
            else display.ShowWaiting("Restarting input");
            Assert.That(left.gameObject.activeSelf, Is.False, "Reset must happen during the stop call, not LateUpdate.");
            Assert.That(right.gameObject.activeSelf, Is.False);
            display.PresentFrame(texture); // Restart before the next Update/LateUpdate.
            yield return null;
            yield return null;
            Assert.That(left.gameObject.activeInHierarchy, Is.False, "No sample belongs to the restarted input yet.");
            Assert.That(right.gameObject.activeInHierarchy, Is.False);

            // A pending async observation from the previous input must also be rejected.
            GameEventBus.OnMalletsTracked?.Invoke(previousLeft, previousRight);
            yield return null;
            yield return null;
            Assert.That(left.gameObject.activeSelf, Is.False);
            Assert.That(right.gameObject.activeSelf, Is.False);
            GameEventBus.OnMalletsTracked?.Invoke(State(0), State(1));
            yield return null;
            yield return null;
            Assert.That(left.gameObject.activeInHierarchy, Is.True, "Fresh input must recover normally.");
            Assert.That(right.gameObject.activeInHierarchy, Is.True);
        }

        display.ShowWaiting();
        Object.Destroy(texture);
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator OrderedFailureRejectsOldDataButStillHidesCurrentFailures()
    {
        EditorSceneManager.OpenScene("Assets/Presentation/Scenes/CameraDisplayPreview.unity");
        yield return new EnterPlayMode();
        yield return null;
        yield return null;
        Object.FindFirstObjectByType<MalletCursorPreviewSource>().enabled = false;
        Object.FindFirstObjectByType<CameraDisplayPreviewSource>().enabled = false;
        CameraDisplayView display = Object.FindFirstObjectByType<CameraDisplayView>();
        MalletCursorPresenter cursors = Object.FindFirstObjectByType<MalletCursorPresenter>();
        Texture2D texture = new Texture2D(320, 180);
        display.PresentFrame(texture);
        cursors.SetDebugCursorsEnabled(true);
        MalletState current = State(0);
        GameEventBus.OnMalletsTracked?.Invoke(current, State(1));
        yield return null;
        yield return null;
        RectTransform left = FindCursor(display, "LeftCursor");
        RectTransform right = FindCursor(display, "RightCursor");
        Assert.That(left.gameObject.activeInHierarchy, Is.True);

        // Legacy notifications have no observation time and cannot override ordered state.
        GameEventBus.OnTrackingLost?.Invoke(0);
        Assert.That(left.gameObject.activeSelf, Is.True);
        MalletState oldFailure = current;
        oldFailure.TimestampMs -= 10;
        oldFailure.Confidence = 0;
        GameEventBus.PublishTrackingLost(0, oldFailure.TimestampMs);
        GameEventBus.OnMalletsTracked?.Invoke(oldFailure, State(1));
        yield return null;
        yield return null;
        Assert.That(left.gameObject.activeInHierarchy, Is.True, "An older failed observation must not replace newer success.");

        GameEventBus.PublishTrackingLost(0, double.NaN);
        GameEventBus.PublishTrackingLost(0, NowMs() + 1000);
        Assert.That(left.gameObject.activeSelf, Is.True, "Unusable failure timestamps cannot override valid state.");
        GameEventBus.PublishTrackingLost(0, current.TimestampMs);
        Assert.That(left.gameObject.activeSelf, Is.False, "Failure at the current observation time is authoritative.");
        Assert.That(right.gameObject.activeSelf, Is.True);
        GameEventBus.OnMalletsTracked?.Invoke(current, State(1));
        yield return null;
        yield return null;
        Assert.That(left.gameObject.activeSelf, Is.False, "Replaying the failed observation cannot resurrect it.");
        GameEventBus.OnMalletsTracked?.Invoke(State(0), State(1));
        yield return null;
        yield return null;
        Assert.That(left.gameObject.activeInHierarchy, Is.True);
        GameEventBus.PublishTrackingLost(0, NowMs());
        Assert.That(left.gameObject.activeSelf, Is.False, "A new failure must still hide immediately.");
        Assert.That(right.gameObject.activeSelf, Is.True);
        display.ShowWaiting();
        Object.Destroy(texture);
        yield return new ExitPlayMode();
    }

    [Test]
    public void FailurePublisherPreservesObservationTimeAndLegacySubscribers()
    {
        int orderedId = -1;
        int legacyId = -1;
        double observedTime = -1;
        Action<int, double> ordered = (id, timestamp) => { orderedId = id; observedTime = timestamp; };
        Action<int> legacy = id => legacyId = id;
        GameEventBus.OnTrackingLostAt += ordered;
        GameEventBus.OnTrackingLost += legacy;
        try
        {
            GameEventBus.PublishTrackingLost(1, 1234.5);
            Assert.That(orderedId, Is.EqualTo(1));
            Assert.That(legacyId, Is.EqualTo(1));
            Assert.That(observedTime, Is.EqualTo(1234.5), "Publication must not replace capture time with arrival time.");
        }
        finally
        {
            GameEventBus.OnTrackingLostAt -= ordered;
            GameEventBus.OnTrackingLost -= legacy;
        }
    }

    private static RectTransform FindCursor(CameraDisplayView display, string name) =>
        display.transform.Find("PreviewFrame/VideoViewport/MirrorRoot/OverlayRoot/" + name) as RectTransform;

    private static MalletState State(int id) => new MalletState
    {
        MalletId = id,
        Position = new Vector3(id == 0 ? -0.5f : 0.5f, 0, 5),
        Confidence = 1,
        TimestampMs = NowMs()
    };

    private static double NowMs() => Time.realtimeSinceStartupAsDouble * 1000.0;
}
