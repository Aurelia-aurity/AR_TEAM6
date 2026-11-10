using System.Collections;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class CameraDisplayCursorTests
{
    [UnityTest]
    public IEnumerator DebugCursorsProjectIndependentMalletsAndMirrorOnlyTheirDisplay()
    {
        EditorSceneManager.OpenScene("Assets/Presentation/Scenes/CameraDisplayPreview.unity");
        yield return new EnterPlayMode();
        yield return null;
        yield return null;
        CameraDisplayView display = Object.FindFirstObjectByType<CameraDisplayView>();
        MalletCursorPresenter cursors = Object.FindFirstObjectByType<MalletCursorPresenter>();
        MalletCursorPreviewSource simulator = Object.FindFirstObjectByType<MalletCursorPreviewSource>();
        RectTransform left = FindCursor(display, "LeftCursor");
        RectTransform right = FindCursor(display, "RightCursor");
        Assert.That(cursors.DebugCursorsEnabled, Is.False);
        Publish(cursors.ProjectionCamera);
        yield return null;
        yield return null;
        Assert.That(left.gameObject.activeSelf, Is.False);
        Assert.That(right.gameObject.activeSelf, Is.False);

        CameraDisplayPreviewPlayTests.ClickToggleLabel(display, "CursorDebugToggle");
        yield return null;
        yield return null;
        Assert.That(cursors.DebugCursorsEnabled, Is.True);
        Assert.That(left.gameObject.activeInHierarchy, Is.True);
        Assert.That(right.gameObject.activeInHierarchy, Is.True);
        AssertCursorsRendered(display, left, right);
        CameraDisplayPreviewPlayTests.CaptureState(display, "cursors");
        simulator.enabled = false;
        Camera projection = cursors.ProjectionCamera;
        // Include camera pose in the projection test, independently of the UI camera.
        projection.transform.SetPositionAndRotation(new Vector3(2, 1, -3), Quaternion.Euler(0, 20, 0));
        Publish(projection, reverseArguments: true);
        yield return null;
        yield return null;
        Assert.That(Vector2.Distance(left.anchorMin, new Vector2(0.25f, 0.25f)), Is.LessThan(0.001f));
        Assert.That(Vector2.Distance(right.anchorMin, new Vector2(0.75f, 0.75f)), Is.LessThan(0.001f));
        Canvas.ForceUpdateCanvases();
        Vector2 before = RectTransformUtility.WorldToScreenPoint(null, left.position);
        Vector3 cameraPosition = projection.transform.position;
        RectTransform viewport = display.transform.Find("PreviewFrame/VideoViewport") as RectTransform;
        Vector3[] bounds = new Vector3[4];
        viewport.GetWorldCorners(bounds);
        float axisSum = RectTransformUtility.WorldToScreenPoint(null, bounds[0]).x
            + RectTransformUtility.WorldToScreenPoint(null, bounds[2]).x;
        display.SetMirrorMode(true);
        Canvas.ForceUpdateCanvases();
        Vector2 after = RectTransformUtility.WorldToScreenPoint(null, left.position);
        Assert.That(after.x, Is.EqualTo(axisSum - before.x).Within(0.01f));
        Assert.That(after.y, Is.EqualTo(before.y).Within(0.01f));
        Assert.That(Vector2.Distance(left.anchorMin, new Vector2(0.25f, 0.25f)), Is.LessThan(0.001f));
        Assert.That(projection.transform.position, Is.EqualTo(cameraPosition));
        AssertCursorsRendered(display, left, right);
        CameraDisplayPreviewPlayTests.CaptureState(display, "cursors-mirrored");

        GameEventBus.PublishTrackingLost(0, NowMs());
        Assert.That(left.gameObject.activeSelf, Is.False);
        Assert.That(right.gameObject.activeSelf, Is.True);
        Publish(projection);
        yield return null;
        yield return null;
        Assert.That(left.gameObject.activeSelf, Is.True);
        CameraDisplayPreviewPlayTests.ClickToggleLabel(display, "CursorDebugToggle");
        Assert.That(left.gameObject.activeSelf, Is.False);
        Assert.That(right.gameObject.activeSelf, Is.False);
        Publish(projection);
        yield return null;
        yield return null;
        Assert.That(left.gameObject.activeSelf, Is.False);
        Assert.That(right.gameObject.activeSelf, Is.False);
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator InvalidStaleAndInterruptedTrackingCannotLeaveOldCursorsVisible()
    {
        EditorSceneManager.OpenScene("Assets/Presentation/Scenes/CameraDisplayPreview.unity");
        yield return new EnterPlayMode();
        yield return null;
        Object.FindFirstObjectByType<MalletCursorPreviewSource>().enabled = false;
        CameraDisplayView display = Object.FindFirstObjectByType<CameraDisplayView>();
        MalletCursorPresenter cursors = Object.FindFirstObjectByType<MalletCursorPresenter>();
        RectTransform left = FindCursor(display, "LeftCursor");
        RectTransform right = FindCursor(display, "RightCursor");
        Camera projection = cursors.ProjectionCamera;
        cursors.SetDebugCursorsEnabled(true);
        Publish(projection);
        yield return null;
        yield return null;
        Assert.That(left.gameObject.activeSelf, Is.True);
        Assert.That(right.gameObject.activeSelf, Is.True);

        foreach (MalletState invalid in new[]
        {
            State(0, Vector3.forward * 5, 0),
            State(0, new Vector3(100, 0, 5)),
            State(0, new Vector3(0, 0, -5)),
            State(0, new Vector3(float.NaN, 0, 5))
        })
        {
            MalletState sample = invalid;
            sample.TimestampMs = NowMs();
            GameEventBus.OnMalletsTracked?.Invoke(sample, State(1, Vector3.forward * 5));
            yield return null;
            yield return null;
            Assert.That(left.gameObject.activeSelf, Is.False);
            Assert.That(right.gameObject.activeSelf, Is.True);
        }

        MalletState fresh = State(0, Vector3.forward * 5);
        GameEventBus.OnMalletsTracked?.Invoke(fresh, State(1, Vector3.forward * 5));
        yield return null;
        yield return null;
        MalletState olderFailure = fresh;
        olderFailure.TimestampMs -= 10;
        olderFailure.Confidence = 0;
        GameEventBus.OnMalletsTracked?.Invoke(olderFailure, State(1, Vector3.forward * 5));
        yield return null;
        yield return null;
        Assert.That(left.gameObject.activeSelf, Is.True, "Late old samples must not replace newer data.");

        cursors.SetDebugCursorsEnabled(false);
        cursors.SetDebugCursorsEnabled(true);
        MalletState stale = State(0, Vector3.forward * 5);
        stale.TimestampMs -= 1000;
        GameEventBus.OnMalletsTracked?.Invoke(stale, State(1, Vector3.forward * 5));
        yield return null;
        yield return null;
        Assert.That(left.gameObject.activeSelf, Is.False, "An old frame arriving now is still stale.");
        MalletState future = State(0, Vector3.forward * 5);
        future.TimestampMs += 1000;
        GameEventBus.OnMalletsTracked?.Invoke(future, State(1, Vector3.forward * 5));
        yield return null;
        yield return null;
        Assert.That(left.gameObject.activeSelf, Is.False);
        Publish(projection);
        yield return null;
        yield return null;
        Assert.That(left.gameObject.activeSelf, Is.True, "Invalid future data must not block recovery.");
        yield return new WaitForSecondsRealtime(0.6f);
        Assert.That(left.gameObject.activeSelf, Is.False, "Silence must expire old cursor positions.");
        Assert.That(right.gameObject.activeSelf, Is.False);

        cursors.SetProjectionCamera(null);
        Publish(projection);
        yield return null;
        yield return null;
        Assert.That(left.gameObject.activeSelf, Is.False);
        cursors.SetProjectionCamera(projection);
        Publish(projection);
        yield return null;
        yield return null;
        Assert.That(left.gameObject.activeSelf, Is.True);

        CameraDisplayPreviewSource video = Object.FindFirstObjectByType<CameraDisplayPreviewSource>();
        video.enabled = false;
        yield return null;
        yield return null;
        Assert.That(left.gameObject.activeSelf, Is.False);
        video.enabled = true;
        yield return null;
        yield return null;
        Assert.That(left.gameObject.activeSelf, Is.False, "Input recovery requires a fresh tracked sample.");
        Publish(projection);
        yield return null;
        yield return null;
        Assert.That(left.gameObject.activeSelf, Is.True);
        cursors.enabled = false;
        Publish(projection);
        Assert.That(left.gameObject.activeSelf, Is.False);
        cursors.enabled = true;
        yield return null;
        yield return null;
        Assert.That(left.gameObject.activeSelf, Is.False);
        Publish(projection);
        yield return null;
        yield return null;
        Assert.That(left.gameObject.activeSelf, Is.True);
        yield return new ExitPlayMode();
    }

    private static RectTransform FindCursor(CameraDisplayView display, string name) =>
        display.transform.Find("PreviewFrame/VideoViewport/MirrorRoot/OverlayRoot/" + name) as RectTransform;

    private static void AssertCursorsRendered(CameraDisplayView display, params RectTransform[] cursors)
    {
        Canvas canvas = display.GetComponent<Canvas>();
        Camera camera = Camera.main;
        RenderTexture target = CameraDisplayViewTests.PrepareCapture(canvas, camera, 1280, 720);
        Texture2D screenshot = CameraDisplayViewTests.ReadCapture(camera, target);
        try
        {
            foreach (RectTransform cursor in cursors)
            {
                Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, cursor.position);
                Color center = screenshot.GetPixel(Mathf.RoundToInt(point.x), Mathf.RoundToInt(point.y));
                Assert.That(center.r, Is.GreaterThan(0.8f), cursor.name + " must render its white center.");
                Assert.That(center.g, Is.GreaterThan(0.8f), cursor.name + " must render its white center.");
                Assert.That(center.b, Is.GreaterThan(0.8f), cursor.name + " must render its white center.");
            }
        }
        finally
        {
            Object.DestroyImmediate(screenshot);
            CameraDisplayViewTests.ReleaseCapture(canvas, camera, target);
        }
    }

    private static void Publish(Camera camera, bool reverseArguments = false)
    {
        // Explicit perspective geometry: these camera-local points project to .25/.25 and .75/.75.
        float halfHeight = 5 * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f);
        float halfWidth = halfHeight * camera.aspect;
        MalletState left = State(0, camera.transform.TransformPoint(new Vector3(-halfWidth * 0.5f, -halfHeight * 0.5f, 5)));
        MalletState right = State(1, camera.transform.TransformPoint(new Vector3(halfWidth * 0.5f, halfHeight * 0.5f, 5)));
        GameEventBus.OnMalletsTracked?.Invoke(reverseArguments ? right : left, reverseArguments ? left : right);
    }

    private static MalletState State(int id, Vector3 position, float confidence = 1) =>
        new MalletState { MalletId = id, Position = position, Confidence = confidence, TimestampMs = NowMs() };

    private static double NowMs() => Time.realtimeSinceStartupAsDouble * 1000.0;
}
