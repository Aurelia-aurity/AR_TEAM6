using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

public sealed class CameraDisplayViewTests
{
    private GameObject root;
    private CameraDisplayView view;
    private RawImage image;
    private Texture2D texture;
    private Camera camera;

    [SetUp]
    public void SetUp()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Presentation/Prefabs/CameraDisplay.prefab"));
        view = root.GetComponent<CameraDisplayView>();
        Assert.That(view, Is.Not.Null, "The reusable prefab must have the view wired.");
        image = root.GetComponentInChildren<RawImage>(true);
        view.ShowWaiting();
        camera = new GameObject("RenderCamera", typeof(Camera)).GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        texture = CreateCorners(160, 90);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(root);
        Object.DestroyImmediate(texture);
        if (camera != null) Object.DestroyImmediate(camera.gameObject);
    }

    [TestCase(0, false, false, "RGBY")]
    [TestCase(90, false, false, "BRYG")]
    [TestCase(180, false, false, "YBGR")]
    [TestCase(270, false, false, "GYRB")]
    [TestCase(0, true, false, "BYRG")]
    [TestCase(90, true, false, "RBGY")]
    [TestCase(180, true, false, "GRYB")]
    [TestCase(270, true, false, "YGBR")]
    [TestCase(0, false, true, "GRYB")]
    [TestCase(90, false, true, "RBGY")]
    [TestCase(180, false, true, "BYRG")]
    [TestCase(270, false, true, "YGBR")]
    [TestCase(0, true, true, "YBGR")]
    [TestCase(90, true, true, "BRYG")]
    [TestCase(180, true, true, "RGBY")]
    [TestCase(270, true, true, "GYRB")]
    public void RenderedCornersFollowInputOrientation(int rotation, bool flipY, bool mirror, string expected)
    {
        Canvas canvas = root.GetComponent<Canvas>();
        RenderTexture target = PrepareCapture(canvas, camera, 1280, 720);
        view.SetMirrorMode(mirror);
        view.PresentFrame(texture, rotation, flipY);
        Canvas.ForceUpdateCanvases();
        Texture2D screenshot = ReadCapture(camera, target);
        try
        {
            RectTransform viewport = root.transform.Find("PreviewFrame/VideoViewport") as RectTransform;
            Vector3[] corners = new Vector3[4];
            viewport.GetWorldCorners(corners); // bottom-left, top-left, top-right, bottom-right
            Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            Vector2 topRight = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            Vector2[] samplePoints =
            {
                new Vector2(0.2f, 0.8f), new Vector2(0.8f, 0.8f),
                new Vector2(0.2f, 0.2f), new Vector2(0.8f, 0.2f)
            };
            for (int i = 0; i < samplePoints.Length; i++)
            {
                int x = Mathf.RoundToInt(Mathf.Lerp(bottomLeft.x, topRight.x, samplePoints[i].x));
                int y = Mathf.RoundToInt(Mathf.Lerp(bottomLeft.y, topRight.y, samplePoints[i].y));
                Color actual = screenshot.GetPixel(x, y);
                Color wanted = CornerColor(expected[i]);
                Assert.That(Vector3.Distance(new Vector3(actual.r, actual.g, actual.b),
                    new Vector3(wanted.r, wanted.g, wanted.b)), Is.LessThan(0.2f),
                    "Incorrect displayed corner " + i + " for rotation " + rotation + ", flip " + flipY + ", mirror " + mirror
                    + ": got " + actual + ", expected " + wanted);
            }
        }
        finally
        {
            Object.DestroyImmediate(screenshot);
            ReleaseCapture(canvas, camera, target);
        }
    }

    [TestCase(640, 360, 0)]
    [TestCase(640, 480, 0)]
    [TestCase(360, 640, 0)]
    [TestCase(640, 360, 90)]
    public void VideoFitsWithoutStretchingOrCropping(int width, int height, int rotation)
    {
        Object.DestroyImmediate(texture);
        texture = CreateCorners(width, height);
        Canvas canvas = root.GetComponent<Canvas>();
        RenderTexture target = PrepareCapture(canvas, camera, 1024, 768);
        try
        {
            view.PresentFrame(texture, rotation);
            Canvas.ForceUpdateCanvases();
            RectTransform viewport = root.transform.Find("PreviewFrame/VideoViewport") as RectTransform;
            RectTransform frame = viewport.parent as RectTransform;
            float expectedRatio = rotation == 90 ? height / (float)width : width / (float)height;
            Assert.That(viewport.rect.width / viewport.rect.height, Is.EqualTo(expectedRatio).Within(0.001));
            Assert.That(viewport.rect.width, Is.LessThanOrEqualTo(frame.rect.width + 0.01f));
            Assert.That(viewport.rect.height, Is.LessThanOrEqualTo(frame.rect.height + 0.01f));
            Vector3[] corners = new Vector3[4];
            image.rectTransform.GetWorldCorners(corners);
            Bounds bounds = new Bounds(viewport.InverseTransformPoint(corners[0]), Vector3.zero);
            for (int i = 1; i < 4; i++) bounds.Encapsulate(viewport.InverseTransformPoint(corners[i]));
            Assert.That(bounds.size.x, Is.EqualTo(viewport.rect.width).Within(0.01f));
            Assert.That(bounds.size.y, Is.EqualTo(viewport.rect.height).Within(0.01f));
        }
        finally { ReleaseCapture(canvas, camera, target); }
    }

    [Test]
    public void InputLossAndErrorsClearOldVideoAndCanRecover()
    {
        Assert.That(view.PresentFrame(texture), Is.True);
        Assert.That(image.enabled, Is.True);
        view.ShowError("Input disconnected.");
        Assert.That(view.Status, Is.EqualTo(CameraDisplayStatus.Error));
        Assert.That(image.texture, Is.Null);
        Assert.That(image.enabled, Is.False);
        Assert.That(root.transform.Find("PreviewFrame/VideoViewport/WaitingPlaceholder").gameObject.activeSelf, Is.True);
        Assert.That(root.transform.Find("Footer/StatusDetail").GetComponent<Text>().text, Is.EqualTo("Input disconnected."));
        Assert.That(view.PresentFrame(texture, 90, true), Is.True);
        Assert.That(view.Status, Is.EqualTo(CameraDisplayStatus.Streaming));
        Assert.That(view.PresentFrame(null), Is.False);
        Assert.That(view.Status, Is.EqualTo(CameraDisplayStatus.Waiting));
        Assert.That(image.texture, Is.Null);
        Assert.That(texture.width, Is.EqualTo(160), "The view must not destroy the provider's texture.");
    }

    [Test]
    public void InvalidOrientationDoesNotLeaveThePreviousFrameOnScreen()
    {
        view.PresentFrame(texture);
        Assert.That(view.PresentFrame(texture, 45), Is.False);
        Assert.That(view.Status, Is.EqualTo(CameraDisplayStatus.Error));
        Assert.That(image.enabled, Is.False);
        Assert.That(image.texture, Is.Null);
    }

    [Test]
    public void MirrorMovesOnlyDisplayedOverlayAndLeavesSourceCoordinatesUnchanged()
    {
        Canvas canvas = root.GetComponent<Canvas>();
        RenderTexture target = PrepareCapture(canvas, camera, 1280, 720);
        GameObject worldObject = new GameObject("Unmodified world object");
        Vector3 worldPosition = new Vector3(1.2f, -0.7f, 4);
        worldObject.transform.position = worldPosition;
        try
        {
            view.PresentFrame(texture);
            RectTransform viewport = root.transform.Find("PreviewFrame/VideoViewport") as RectTransform;
            Transform overlay = viewport.Find("MirrorRoot/OverlayRoot");
            RectTransform marker = new GameObject("Test marker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image))
                .GetComponent<RectTransform>();
            marker.SetParent(overlay, false);
            Vector2 projectedPosition = new Vector2(0.2f, 0.7f);
            marker.anchorMin = marker.anchorMax = projectedPosition;
            marker.anchoredPosition = Vector2.zero;
            marker.sizeDelta = new Vector2(18, 18);
            marker.GetComponent<Image>().color = Color.magenta;
            Canvas.ForceUpdateCanvases();
            Vector2 before = RectTransformUtility.WorldToScreenPoint(camera, marker.position);
            Vector3 titleBefore = root.transform.Find("Header/Title").position;
            Vector3[] corners = new Vector3[4];
            viewport.GetWorldCorners(corners);
            float left = RectTransformUtility.WorldToScreenPoint(camera, corners[0]).x;
            float right = RectTransformUtility.WorldToScreenPoint(camera, corners[2]).x;

            view.SetMirrorMode(true);
            Canvas.ForceUpdateCanvases();
            Vector2 after = RectTransformUtility.WorldToScreenPoint(camera, marker.position);
            Assert.That(after.x, Is.EqualTo(left + right - before.x).Within(0.01f));
            Assert.That(after.y, Is.EqualTo(before.y).Within(0.01f));
            Assert.That(marker.anchorMin, Is.EqualTo(projectedPosition));
            Assert.That(marker.anchoredPosition, Is.EqualTo(Vector2.zero));
            Assert.That(worldObject.transform.position, Is.EqualTo(worldPosition));
            Assert.That(root.transform.Find("Header/Title").position, Is.EqualTo(titleBefore));
            Assert.That(texture.GetPixel(0, 0), Is.EqualTo(Color.blue));
            Texture2D screenshot = ReadCapture(camera, target);
            Color markerColor = screenshot.GetPixel(Mathf.RoundToInt(after.x), Mathf.RoundToInt(after.y));
            Object.DestroyImmediate(screenshot);
            Assert.That(markerColor.r, Is.GreaterThan(0.8f));
            Assert.That(markerColor.b, Is.GreaterThan(0.8f));
            Assert.That(markerColor.g, Is.LessThan(0.2f));

            view.SetMirrorMode(false);
            Canvas.ForceUpdateCanvases();
            Vector2 restored = RectTransformUtility.WorldToScreenPoint(camera, marker.position);
            Assert.That(Vector2.Distance(restored, before), Is.LessThan(0.01f));
        }
        finally
        {
            Object.DestroyImmediate(worldObject);
            ReleaseCapture(canvas, camera, target);
        }
    }

    [Test]
    public void MirrorSelectionSurvivesInputUpdatesAndRecovery()
    {
        Toggle toggle = root.GetComponentInChildren<Toggle>(true);
        view.SetMirrorMode(true);
        view.PresentFrame(texture);
        view.PresentFrame(texture, 90, true);
        view.ShowError();
        Assert.That(view.MirrorEnabled, Is.True);
        Assert.That(toggle.isOn, Is.True);
        Assert.That(root.transform.Find("PreviewFrame/VideoViewport/MirrorRoot").gameObject.activeSelf, Is.False);
        view.ShowWaiting();
        view.PresentFrame(texture);
        Assert.That(view.MirrorEnabled, Is.True);
        Assert.That(toggle.isOn, Is.True);
        Assert.That(root.transform.Find("PreviewFrame/VideoViewport/MirrorRoot").gameObject.activeSelf, Is.True);
        view.SetMirrorMode(false);
        Assert.That(toggle.isOn, Is.False);
    }

    private static Texture2D CreateCorners(int width, int height)
    {
        Texture2D result = new Texture2D(width, height, TextureFormat.RGBA32, false);
        result.filterMode = FilterMode.Point;
        Color[] pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                pixels[y * width + x] = y >= height / 2
                    ? (x < width / 2 ? Color.red : Color.green)
                    : (x < width / 2 ? Color.blue : Color.yellow);
        result.SetPixels(pixels);
        result.Apply();
        return result;
    }

    private static Color CornerColor(char corner)
    {
        switch (corner)
        {
            case 'R': return Color.red;
            case 'G': return Color.green;
            case 'B': return Color.blue;
            default: return Color.yellow;
        }
    }

    internal static RenderTexture PrepareCapture(Canvas canvas, Camera camera, int width, int height)
    {
        RenderTexture target = new RenderTexture(width, height, 24);
        camera.targetTexture = target;
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 1;
        Canvas.ForceUpdateCanvases();
        return target;
    }

    internal static Texture2D ReadCapture(Camera camera, RenderTexture target)
    {
        camera.Render();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        Texture2D output = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        output.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
        output.Apply();
        RenderTexture.active = previous;
        return output;
    }

    internal static void ReleaseCapture(Canvas canvas, Camera camera, RenderTexture target)
    {
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.worldCamera = null;
        camera.targetTexture = null;
        target.Release();
        Object.DestroyImmediate(target);
    }
}

public sealed class CameraDisplayPreviewPlayTests
{
    [UnityTest]
    public IEnumerator PreviewSceneDisplaysTestInputAndRecoversFromError()
    {
        EditorSceneManager.OpenScene("Assets/Presentation/Scenes/CameraDisplayPreview.unity");
        yield return new EnterPlayMode();
        yield return null;
        yield return null;
        CameraDisplayView view = Object.FindFirstObjectByType<CameraDisplayView>();
        Assert.That(view.Status, Is.EqualTo(CameraDisplayStatus.Streaming));
        Toggle toggle = view.GetComponentInChildren<Toggle>(true);
        Assert.That(toggle.interactable, Is.True);
        Assert.That(view.MirrorEnabled, Is.False);
        CaptureState(view, "streaming");
        ClickToggleLabel(view, "MirrorToggle");
        yield return null;
        Assert.That(view.MirrorEnabled, Is.True);
        Assert.That(toggle.isOn, Is.True);
        CaptureState(view, "mirror-on");
        ClickToggleLabel(view, "MirrorToggle");
        Assert.That(view.MirrorEnabled, Is.False);
        ClickToggleLabel(view, "MirrorToggle");
        Assert.That(view.MirrorEnabled, Is.True);

        CameraDisplayPreviewSource source = Object.FindFirstObjectByType<CameraDisplayPreviewSource>();
        SerializedObject settings = new SerializedObject(source);
        settings.FindProperty("inputMode").enumValueIndex = 2;
        settings.ApplyModifiedProperties();
        yield return null;
        yield return null;
        Assert.That(view.Status, Is.EqualTo(CameraDisplayStatus.Error));
        Assert.That(view.MirrorEnabled, Is.True);
        CaptureState(view, "error");

        settings.Update();
        settings.FindProperty("inputMode").enumValueIndex = 1;
        settings.ApplyModifiedProperties();
        yield return null;
        yield return null;
        Assert.That(view.Status, Is.EqualTo(CameraDisplayStatus.Waiting));
        Assert.That(toggle.isOn, Is.True);
        CaptureState(view, "waiting");

        settings.Update();
        settings.FindProperty("inputMode").enumValueIndex = 0;
        settings.ApplyModifiedProperties();
        yield return null;
        yield return null;
        Assert.That(view.Status, Is.EqualTo(CameraDisplayStatus.Streaming));
        Assert.That(view.MirrorEnabled, Is.True);
        view.enabled = false;
        yield return null;
        Assert.That(view.GetComponentInChildren<RawImage>(true).texture, Is.Null);
        Assert.That(toggle.interactable, Is.False);
        view.enabled = true;
        yield return null;
        yield return null;
        Assert.That(view.Status, Is.EqualTo(CameraDisplayStatus.Streaming));
        Assert.That(toggle.interactable, Is.True);
        Assert.That(toggle.isOn, Is.True);
        ClickToggleLabel(view, "MirrorToggle");
        Assert.That(view.MirrorEnabled, Is.False);
        ClickToggleLabel(view, "MirrorToggle");
        Assert.That(view.MirrorEnabled, Is.True);
        yield return new ExitPlayMode();
    }

    internal static void ClickToggleLabel(CameraDisplayView view, string toggleName)
    {
        Canvas.ForceUpdateCanvases();
        RectTransform label = view.transform.Find("Footer/" + toggleName + "/Label") as RectTransform;
        PointerEventData pointer = new PointerEventData(EventSystem.current)
        {
            button = PointerEventData.InputButton.Left,
            position = RectTransformUtility.WorldToScreenPoint(null, label.TransformPoint(label.rect.center))
        };
        List<RaycastResult> hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointer, hits);
        Assert.That(hits.Count, Is.GreaterThan(0), "The Toggle label must be clickable: " + toggleName);
        Assert.That(hits[0].gameObject, Is.EqualTo(label.gameObject));
        Assert.That(ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler),
            Is.Not.Null);
    }

    internal static void CaptureState(CameraDisplayView view, string state)
    {
        Canvas canvas = view.GetComponent<Canvas>();
        Camera camera = Camera.main;
        RenderTexture target = CameraDisplayViewTests.PrepareCapture(canvas, camera, 1280, 720);
        Texture2D capture = CameraDisplayViewTests.ReadCapture(camera, target);
        Directory.CreateDirectory("Logs");
        File.WriteAllBytes("Logs/camera-display-" + state + ".png", capture.EncodeToPNG());
        Object.DestroyImmediate(capture);
        CameraDisplayViewTests.ReleaseCapture(canvas, camera, target);
    }
}
