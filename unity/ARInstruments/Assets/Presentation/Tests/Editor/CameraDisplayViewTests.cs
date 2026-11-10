using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
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

    [TestCase(0, false, "RGBY")]
    [TestCase(90, false, "BRYG")]
    [TestCase(180, false, "YBGR")]
    [TestCase(270, false, "GYRB")]
    [TestCase(0, true, "BYRG")]
    [TestCase(90, true, "RBGY")]
    [TestCase(180, true, "GRYB")]
    [TestCase(270, true, "YGBR")]
    public void RenderedCornersFollowInputOrientation(int rotation, bool flipY, string expected)
    {
        Canvas canvas = root.GetComponent<Canvas>();
        RenderTexture target = PrepareCapture(canvas, camera, 1280, 720);
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
                    "Incorrect displayed corner " + i + " for rotation " + rotation + ", flip " + flipY
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
        Assert.That(view.GetComponentInChildren<Toggle>(true).interactable, Is.False);
        CaptureState(view, "streaming");

        CameraDisplayPreviewSource source = Object.FindFirstObjectByType<CameraDisplayPreviewSource>();
        SerializedObject settings = new SerializedObject(source);
        settings.FindProperty("inputMode").enumValueIndex = 2;
        settings.ApplyModifiedProperties();
        yield return null;
        yield return null;
        Assert.That(view.Status, Is.EqualTo(CameraDisplayStatus.Error));
        CaptureState(view, "error");

        settings.Update();
        settings.FindProperty("inputMode").enumValueIndex = 1;
        settings.ApplyModifiedProperties();
        yield return null;
        yield return null;
        Assert.That(view.Status, Is.EqualTo(CameraDisplayStatus.Waiting));
        CaptureState(view, "waiting");

        settings.Update();
        settings.FindProperty("inputMode").enumValueIndex = 0;
        settings.ApplyModifiedProperties();
        yield return null;
        yield return null;
        Assert.That(view.Status, Is.EqualTo(CameraDisplayStatus.Streaming));
        view.enabled = false;
        yield return null;
        Assert.That(view.GetComponentInChildren<RawImage>(true).texture, Is.Null);
        view.enabled = true;
        yield return null;
        yield return null;
        Assert.That(view.Status, Is.EqualTo(CameraDisplayStatus.Streaming));
        yield return new ExitPlayMode();
    }

    private static void CaptureState(CameraDisplayView view, string state)
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
