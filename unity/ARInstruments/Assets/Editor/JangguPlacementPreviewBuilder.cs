using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Authors a separate preview scene; existing scenes and prefabs are preserved.</summary>
public static class JangguPlacementPreviewBuilder
{
    public const string ScenePath = "Assets/Presentation/Scenes/JangguPlacementPreview.unity";
    private const int PreviewLayer = 31;
    private static readonly Color TextColor = new Color32(227, 235, 242, 255);

    [MenuItem("ARInstruments/Create Janggu placement preview")]
    public static void Create()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            throw new InvalidOperationException("Janggu placement scene already exists; preserve manual edits.");

        var scene = EditorSceneManager.OpenScene("Assets/Presentation/Scenes/CameraDisplayPreview.unity");
        CameraDisplayView display = UnityEngine.Object.FindFirstObjectByType<CameraDisplayView>();
        if (display == null) throw new InvalidOperationException("Camera display is missing.");
        Transform canvas = display.transform;
        // Keep at least the reference canvas size so the pose rows and Reset fit on wide screens.
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        PrefabUtility.RecordPrefabInstancePropertyModifications(scaler);
        Text title = canvas.Find("Header/Title").GetComponent<Text>();
        title.text = "JANGGU PLACEMENT";
        PrefabUtility.RecordPrefabInstancePropertyModifications(title);
        RectTransform frame = (RectTransform)canvas.Find("PreviewFrame");
        frame.anchorMin = Vector2.zero;
        frame.anchorMax = Vector2.one;
        frame.offsetMin = new Vector2(332, 104);
        frame.offsetMax = new Vector2(-28, -112);

        var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Presentation/Prefabs/Janggu.prefab");
        if (model == null) throw new InvalidOperationException("Janggu presentation prefab is missing.");
        var instrument = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
        instrument.transform.SetPositionAndRotation(new Vector3(-0.08f, 0, 1.4f), Quaternion.Euler(8, -28, 0));
        foreach (Transform child in instrument.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = PreviewLayer;

        Camera projection = canvas.GetComponent<MalletCursorPresenter>().ProjectionCamera;
        projection.enabled = false;
        projection.clearFlags = CameraClearFlags.SolidColor;
        projection.backgroundColor = Color.clear;
        projection.cullingMask = 1 << PreviewLayer;
        projection.fieldOfView = 38;
        projection.nearClipPlane = 0.05f;
        projection.farClipPlane = 10;
        projection.allowHDR = false;
        projection.allowMSAA = false;
        Camera.main.cullingMask &= ~(1 << PreviewLayer);

        RectTransform mirrorRoot = (RectTransform)canvas.Find("PreviewFrame/VideoViewport/MirrorRoot");
        var overlay = Rect("InstrumentImage", mirrorRoot);
        Stretch(overlay);
        overlay.SetSiblingIndex(1); // Video, instrument, then debug cursor overlay.
        RawImage image = overlay.gameObject.AddComponent<RawImage>();
        image.color = Color.white;
        image.raycastTarget = false;
        image.enabled = false;
        var renderer = overlay.gameObject.AddComponent<InstrumentPreviewRenderer>();
        var renderSettings = new SerializedObject(renderer);
        renderSettings.FindProperty("display").objectReferenceValue = display;
        renderSettings.FindProperty("instrumentCamera").objectReferenceValue = projection;
        renderSettings.FindProperty("output").objectReferenceValue = image;
        renderSettings.ApplyModifiedPropertiesWithoutUndo();

        RectTransform panel = Rect("PlacementPanel", canvas);
        panel.anchorMin = Vector2.zero;
        panel.anchorMax = new Vector2(0, 1);
        panel.offsetMin = new Vector2(24, 104);
        panel.offsetMax = new Vector2(310, -112);
        panel.gameObject.AddComponent<Image>().color = new Color32(22, 32, 46, 255);
        MakeText("Title", panel, "INSTRUMENT POSE", new Vector2(16, -20), new Vector2(254, 24), 17);
        MakeText("Hint", panel, "Position (m) / Rotation (deg)", new Vector2(16, -46), new Vector2(254, 22), 12);

        var positions = new Slider[3];
        var rotations = new Slider[3];
        var labels = new Text[7];
        Vector3 initialAngles = new Vector3(8, -28, 0);
        string[] axes = { "X", "Y", "Z" };
        for (int i = 0; i < 3; i++)
        {
            float min = i == 2 ? 0.7f : -0.6f;
            float max = i == 2 ? 3f : 0.6f;
            positions[i] = MakeSlider(panel, "Position" + axes[i], "Position " + axes[i], i,
                min, max, instrument.transform.position[i], out labels[i]);
            rotations[i] = MakeSlider(panel, "Rotation" + axes[i], "Rotation " + axes[i], i + 3,
                -180, 180, initialAngles[i], out labels[i + 3]);
        }
        Slider scale = MakeSlider(panel, "Scale", "Scale", 6, 0.25f, 2f, 1f, out labels[6]);

        var buttonRect = Rect("ResetPose", panel);
        buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0);
        buttonRect.anchoredPosition = new Vector2(0, 26);
        buttonRect.sizeDelta = new Vector2(254, 34);
        Image buttonImage = buttonRect.gameObject.AddComponent<Image>();
        buttonImage.color = new Color32(48, 93, 107, 255);
        Button reset = buttonRect.gameObject.AddComponent<Button>();
        reset.targetGraphic = buttonImage;
        RectTransform buttonLabel = Rect("Label", buttonRect);
        Stretch(buttonLabel);
        Text text = buttonLabel.gameObject.AddComponent<Text>();
        ConfigureText(text, "Reset pose", 14);
        text.alignment = TextAnchor.MiddleCenter;

        var controls = panel.gameObject.AddComponent<InstrumentPlacementControls>();
        var settings = new SerializedObject(controls);
        settings.FindProperty("instrument").objectReferenceValue = instrument.transform;
        AssignArray(settings.FindProperty("positionSliders"), positions);
        AssignArray(settings.FindProperty("rotationSliders"), rotations);
        AssignArray(settings.FindProperty("valueLabels"), labels);
        settings.FindProperty("scaleSlider").objectReferenceValue = scale;
        settings.FindProperty("resetButton").objectReferenceValue = reset;
        settings.ApplyModifiedPropertiesWithoutUndo();

        if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("Scene save failed.");
        AssetDatabase.SaveAssets();
        Debug.Log("JANGGU_PLACEMENT_CREATED: " + ScenePath);
    }

    private static Slider MakeSlider(Transform parent, string name, string title, int index,
        float min, float max, float value, out Text valueLabel)
    {
        RectTransform row = Rect(name, parent);
        row.anchorMin = new Vector2(0, 1);
        row.anchorMax = Vector2.one;
        row.pivot = new Vector2(0.5f, 1);
        row.anchoredPosition = new Vector2(0, -76 - index * 49);
        row.sizeDelta = new Vector2(-32, 44);
        MakeText("Label", row, title, Vector2.zero, new Vector2(160, 20), 13);
        valueLabel = MakeText("Value", row, value.ToString("F2"), new Vector2(166, 0), new Vector2(88, 20), 13);
        valueLabel.alignment = TextAnchor.UpperRight;
        RectTransform track = Rect("Slider", row);
        track.anchorMin = new Vector2(0, 1);
        track.anchorMax = Vector2.one;
        track.pivot = new Vector2(0.5f, 1);
        track.anchoredPosition = new Vector2(0, -27);
        track.sizeDelta = new Vector2(0, 12);
        Image background = track.gameObject.AddComponent<Image>();
        background.color = new Color32(55, 69, 86, 255);
        Slider slider = track.gameObject.AddComponent<Slider>();
        RectTransform fill = Rect("Fill", track);
        Stretch(fill);
        Image fillImage = fill.gameObject.AddComponent<Image>();
        fillImage.color = new Color32(78, 173, 167, 255);
        fillImage.raycastTarget = false;
        RectTransform handleArea = Rect("HandleArea", track);
        Stretch(handleArea);
        handleArea.offsetMin = new Vector2(7, 0);
        handleArea.offsetMax = new Vector2(-7, 0);
        RectTransform handle = Rect("Handle", handleArea);
        handle.sizeDelta = new Vector2(14, 20);
        Image handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.color = TextColor;
        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.targetGraphic = handleImage;
        slider.minValue = min;
        slider.maxValue = max;
        slider.SetValueWithoutNotify(value);
        return slider;
    }

    private static RectTransform Rect(string name, Transform parent)
    {
        var result = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        result.SetParent(parent, false);
        return result;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static Text MakeText(string name, Transform parent, string value, Vector2 position, Vector2 size, int fontSize)
    {
        RectTransform rect = Rect(name, parent);
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        Text text = rect.gameObject.AddComponent<Text>();
        ConfigureText(text, value, fontSize);
        return text;
    }

    private static void ConfigureText(Text text, string value, int fontSize)
    {
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.color = TextColor;
        text.text = value;
        text.raycastTarget = false;
    }

    private static void AssignArray(SerializedProperty property, UnityEngine.Object[] values)
    {
        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }
}
