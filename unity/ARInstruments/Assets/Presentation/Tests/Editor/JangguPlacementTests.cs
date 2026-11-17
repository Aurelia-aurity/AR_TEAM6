using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

public sealed class JangguPlacementTests
{
    private const string ScenePath = "Assets/Presentation/Scenes/JangguPlacementPreview.unity";
    private const string PrefabPath = "Assets/Presentation/Prefabs/Janggu.prefab";

    [Test]
    public void SceneReusesJangguPrefabAndKeepsWorldPoseOutsideMirror()
    {
        EditorSceneManager.OpenScene(ScenePath);
        var controls = Object.FindFirstObjectByType<InstrumentPlacementControls>();
        var renderer = Object.FindFirstObjectByType<InstrumentPreviewRenderer>(FindObjectsInactive.Include);
        var display = Object.FindFirstObjectByType<CameraDisplayView>();
        Assert.That(controls, Is.Not.Null);
        Assert.That(renderer, Is.Not.Null);
        Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(controls.Instrument.gameObject),
            Is.EqualTo(PrefabPath));
        Transform mirror = display.transform.Find("PreviewFrame/VideoViewport/MirrorRoot");
        Assert.That(mirror.Find("InstrumentImage"), Is.Not.Null);
        Assert.That(controls.Instrument.IsChildOf(mirror), Is.False);
        Assert.That(renderer.InstrumentCamera, Is.SameAs(display.GetComponent<MalletCursorPresenter>().ProjectionCamera));
        var names = controls.Instrument.GetComponentsInChildren<Transform>().Select(t => t.name).ToArray();
        foreach (string region in new[] { "Head_A_Inner", "Head_A_Outer", "Head_B_Inner", "Head_B_Outer" })
            Assert.That(names, Does.Contain(region));
        var original = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.That(original.transform.position, Is.EqualTo(Vector3.zero));
        Assert.That(original.transform.localScale, Is.EqualTo(Vector3.one));
        Assert.That(controls.Instrument.GetComponentsInChildren<Collider>(), Is.Empty);
    }

    [UnityTest]
    public IEnumerator PoseControlsRenderModelAndMirrorOnlyDisplay()
    {
        EditorSceneManager.OpenScene(ScenePath);
        yield return new EnterPlayMode();
        yield return null;
        yield return null;
        var controls = Object.FindFirstObjectByType<InstrumentPlacementControls>();
        var renderer = Object.FindFirstObjectByType<InstrumentPreviewRenderer>();
        var display = Object.FindFirstObjectByType<CameraDisplayView>();
        Assert.That(renderer.OutputTexture, Is.Not.Null);
        AssertVisibleModelOnTransparentBackground(renderer.OutputTexture);
        Vector3 initialPosition = controls.Instrument.position;
        Quaternion initialRotation = controls.Instrument.rotation;
        Vector3 initialScale = controls.Instrument.localScale;
        Slider positionX = controls.transform.Find("PositionX/Slider").GetComponent<Slider>();
        Slider rotationY = controls.transform.Find("RotationY/Slider").GetComponent<Slider>();
        Slider scale = controls.transform.Find("Scale/Slider").GetComponent<Slider>();
        positionX.value = 0.15f;
        rotationY.value = 35;
        scale.value = 1.25f;
        Assert.That(controls.Instrument.position.x, Is.EqualTo(0.15f).Within(0.001));
        Assert.That(Quaternion.Angle(controls.Instrument.rotation, Quaternion.Euler(8, 35, 0)), Is.LessThan(0.01));
        Assert.That(controls.Instrument.localScale, Is.EqualTo(initialScale * 1.25f));
        controls.transform.Find("ResetPose").GetComponent<Button>().onClick.Invoke();
        Assert.That(controls.Instrument.position, Is.EqualTo(initialPosition));
        Assert.That(Quaternion.Angle(controls.Instrument.rotation, initialRotation), Is.LessThan(0.01));
        Assert.That(controls.Instrument.localScale, Is.EqualTo(initialScale));
        yield return null;

        Canvas canvas = display.GetComponent<Canvas>();
        Camera camera = Camera.main;
        RenderTexture captureTarget = CameraDisplayViewTests.PrepareCapture(canvas, camera, 1280, 720);
        Texture2D normal = null;
        Texture2D mirrored = null;
        try
        {
            display.SetMirrorMode(false);
            Canvas.ForceUpdateCanvases();
            normal = CameraDisplayViewTests.ReadCapture(camera, captureTarget);
            display.SetMirrorMode(true);
            Canvas.ForceUpdateCanvases();
            mirrored = CameraDisplayViewTests.ReadCapture(camera, captureTarget);
            Assert.That(controls.Instrument.position, Is.EqualTo(initialPosition));
            Assert.That(Quaternion.Angle(controls.Instrument.rotation, initialRotation), Is.LessThan(0.01));
            Assert.That(controls.Instrument.localScale, Is.EqualTo(initialScale));
            RectTransform viewport = (RectTransform)display.transform.Find("PreviewFrame/VideoViewport");
            var corners = new Vector3[4];
            viewport.GetWorldCorners(corners);
            Vector2 low = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            Vector2 high = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            float error = 0;
            for (int y = 1; y <= 7; y++)
                for (int x = 1; x <= 9; x++)
                {
                    int firstX = Mathf.RoundToInt(Mathf.Lerp(low.x, high.x, x / 10f));
                    int secondX = Mathf.RoundToInt(Mathf.Lerp(low.x, high.x, 1 - x / 10f));
                    int sampleY = Mathf.RoundToInt(Mathf.Lerp(low.y, high.y, y / 8f));
                    Color a = normal.GetPixel(firstX, sampleY);
                    Color b = mirrored.GetPixel(secondX, sampleY);
                    error += Vector3.Distance(new Vector3(a.r, a.g, a.b), new Vector3(b.r, b.g, b.b));
                }
            Assert.That(error / 63, Is.LessThan(0.08f), "Video and instrument should mirror together.");
            Directory.CreateDirectory("Logs");
            File.WriteAllBytes("Logs/janggu-placement-normal.png", normal.EncodeToPNG());
            File.WriteAllBytes("Logs/janggu-placement-mirrored.png", mirrored.EncodeToPNG());
        }
        finally
        {
            if (normal != null) Object.Destroy(normal);
            if (mirrored != null) Object.Destroy(mirrored);
            CameraDisplayViewTests.ReleaseCapture(canvas, camera, captureTarget);
        }
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator InputLossPortraitAndDisableManagePreviewLifetime()
    {
        EditorSceneManager.OpenScene(ScenePath);
        yield return new EnterPlayMode();
        yield return null;
        yield return null;
        var renderer = Object.FindFirstObjectByType<InstrumentPreviewRenderer>();
        var display = Object.FindFirstObjectByType<CameraDisplayView>();
        var source = Object.FindFirstObjectByType<CameraDisplayPreviewSource>();
        RawImage image = renderer.GetComponent<RawImage>();
        var settings = new SerializedObject(source);
        settings.FindProperty("inputMode").enumValueIndex = 2;
        settings.ApplyModifiedProperties();
        yield return null;
        yield return null;
        Assert.That(display.Status, Is.EqualTo(CameraDisplayStatus.Error));
        Assert.That(image.enabled, Is.False);
        renderer.enabled = false;
        Assert.That(renderer.OutputTexture, Is.Null);
        Assert.That(image.texture, Is.Null);
        Assert.That(renderer.InstrumentCamera.targetTexture, Is.Null);
        settings.Update();
        settings.FindProperty("inputMode").enumValueIndex = 0;
        settings.FindProperty("frameSize").vector2IntValue = new Vector2Int(360, 640);
        settings.ApplyModifiedProperties();
        renderer.enabled = true;
        yield return null;
        yield return null;
        Assert.That(image.enabled, Is.True);
        Assert.That(renderer.OutputTexture.width / (float)renderer.OutputTexture.height,
            Is.EqualTo(360f / 640).Within(0.003));
        AssertVisibleModelOnTransparentBackground(renderer.OutputTexture);
        yield return new ExitPlayMode();
    }

    private static void AssertVisibleModelOnTransparentBackground(RenderTexture target)
    {
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        var pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try
        {
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            pixels.Apply();
            int opaque = 0;
            foreach (Color32 pixel in pixels.GetPixels32()) if (pixel.a > 128) opaque++;
            Assert.That(opaque, Is.GreaterThan(100), "The imported model must actually render.");
            Assert.That(opaque, Is.LessThan(target.width * target.height / 2), "The model background must be transparent.");
            Assert.That(pixels.GetPixel(0, 0).a, Is.LessThan(0.05));
        }
        finally
        {
            RenderTexture.active = previous;
            Object.Destroy(pixels);
        }
    }
}
