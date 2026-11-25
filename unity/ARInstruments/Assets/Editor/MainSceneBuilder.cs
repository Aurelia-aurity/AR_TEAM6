using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Idempotently assembles the integrated A -> B -> C runtime pipeline in Assets/Shared/Scenes/Main.unity.
/// </summary>
public static class MainSceneBuilder
{
    public const string ScenePath = "Assets/Shared/Scenes/Main.unity";
    private const int InstrumentLayer = 31;

    [MenuItem("ARInstruments/Build Main integration scene")]
    public static void BuildMainScene()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        RemoveExistingRoots(scene,
            "ScreenControls",
            "CameraDisplay",
            "InstrumentProjectionCamera",
            "Instruments",
            "TrackingSystem",
            "PerformanceSystem");

        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            throw new InvalidOperationException("Main Camera is missing from Main.unity.");
        }

        mainCamera.cullingMask &= ~(1 << InstrumentLayer);

        Camera projectionCamera = CreateProjectionCamera(scene);
        (GameObject janggu, GameObject buk) = CreateInstruments(scene);
        CreateCameraDisplay(scene, projectionCamera, janggu, buk);
        CreateTrackingSystem(scene);
        CreatePerformanceSystem(scene, janggu.transform, buk.transform);

        if (!EditorSceneManager.SaveScene(scene, ScenePath))
        {
            throw new InvalidOperationException("Failed to save Main.unity.");
        }

        AssetDatabase.SaveAssets();
        Debug.Log("MAIN_SCENE_BUILT: " + ScenePath);
    }

    private static void RemoveExistingRoots(Scene scene, params string[] names)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (Array.IndexOf(names, root.name) >= 0)
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }

    private static Camera CreateProjectionCamera(Scene scene)
    {
        var cameraObject = new GameObject("InstrumentProjectionCamera");
        SceneManager.MoveGameObjectToScene(cameraObject, scene);
        cameraObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        Camera cam = cameraObject.AddComponent<Camera>();
        cam.enabled = false;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.clear;
        cam.cullingMask = 1 << InstrumentLayer;
        cam.fieldOfView = 38f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 10f;
        cam.allowHDR = false;
        cam.allowMSAA = false;

        var urpData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
        urpData.renderPostProcessing = false;
        return cam;
    }

    private static (GameObject janggu, GameObject buk) CreateInstruments(Scene scene)
    {
        var instrumentsRoot = new GameObject("Instruments");
        SceneManager.MoveGameObjectToScene(instrumentsRoot, scene);

        GameObject jangguPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Presentation/Prefabs/Janggu.prefab");
        GameObject bukPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Presentation/Prefabs/Buk.prefab");
        if (jangguPrefab == null || bukPrefab == null)
        {
            throw new InvalidOperationException("Janggu.prefab or Buk.prefab is missing.");
        }

        var janggu = (GameObject)PrefabUtility.InstantiatePrefab(jangguPrefab, instrumentsRoot.transform);
        janggu.name = "Janggu";
        janggu.transform.SetPositionAndRotation(new Vector3(0f, -0.06f, 1.40f), Quaternion.Euler(8f, 0f, 0f));
        SetLayerRecursively(janggu.transform, InstrumentLayer);
        janggu.SetActive(true);

        var buk = (GameObject)PrefabUtility.InstantiatePrefab(bukPrefab, instrumentsRoot.transform);
        buk.name = "Buk";
        buk.transform.SetPositionAndRotation(new Vector3(0f, -0.06f, 1.40f), Quaternion.Euler(10f, -75f, 0f));
        SetLayerRecursively(buk.transform, InstrumentLayer);
        ConfigureBukVibration(buk);
        buk.SetActive(false);

        return (janggu, buk);
    }

    private static void ConfigureBukVibration(GameObject buk)
    {
        if (buk.GetComponent<HitRegionVibration>() != null)
        {
            return;
        }

        var definitions = new[]
        {
            (id: "buk_head_a_inner", node: "Head_A_Inner", normal: Vector3.right),
            (id: "buk_head_a_outer", node: "Head_A_Outer", normal: Vector3.right),
            (id: "buk_head_b_inner", node: "Head_B_Inner", normal: Vector3.left),
            (id: "buk_head_b_outer", node: "Head_B_Outer", normal: Vector3.left)
        };

        Transform[] nodes = buk.GetComponentsInChildren<Transform>(true);
        var vibration = buk.AddComponent<HitRegionVibration>();
        var serialized = new SerializedObject(vibration);
        SerializedProperty regions = serialized.FindProperty("regions");
        regions.arraySize = definitions.Length;
        for (int i = 0; i < definitions.Length; i++)
        {
            SerializedProperty element = regions.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("RegionId").stringValue = definitions[i].id;
            element.FindPropertyRelative("Target").objectReferenceValue = nodes.Single(t => t.name == definitions[i].node);
            element.FindPropertyRelative("LocalNormal").vector3Value = definitions[i].normal;
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void CreateCameraDisplay(Scene scene, Camera projectionCamera, GameObject janggu, GameObject buk)
    {
        GameObject displayPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Presentation/Prefabs/CameraDisplay.prefab");
        if (displayPrefab == null)
        {
            throw new InvalidOperationException("CameraDisplay.prefab is missing.");
        }

        var displayRoot = (GameObject)PrefabUtility.InstantiatePrefab(displayPrefab, scene);
        displayRoot.name = "CameraDisplay";

        Transform canvasTransform = displayRoot.transform;
        var previewFrame = canvasTransform.Find("PreviewFrame") as RectTransform;
        if (previewFrame != null)
        {
            Vector2 offsetMin = previewFrame.offsetMin;
            offsetMin.y = 104f;
            previewFrame.offsetMin = offsetMin;
        }

        var displayView = displayRoot.GetComponent<CameraDisplayView>();
        var framePresenter = displayRoot.AddComponent<CameraFramePresenter>();
        var presenterSo = new SerializedObject(framePresenter);
        presenterSo.FindProperty("cameraId").stringValue = "primary";
        presenterSo.ApplyModifiedPropertiesWithoutUndo();

        var cursorPresenter = displayRoot.GetComponent<MalletCursorPresenter>();
        if (cursorPresenter != null)
        {
            var cursorSo = new SerializedObject(cursorPresenter);
            cursorSo.FindProperty("projectionCamera").objectReferenceValue = projectionCamera;
            cursorSo.ApplyModifiedPropertiesWithoutUndo();
        }

        Transform mirrorRoot = canvasTransform.Find("PreviewFrame/VideoViewport/MirrorRoot");
        if (mirrorRoot != null)
        {
            var instrumentImageObj = new GameObject("InstrumentImage", typeof(RectTransform));
            var imageRect = (RectTransform)instrumentImageObj.transform;
            imageRect.SetParent(mirrorRoot, false);
            imageRect.anchorMin = Vector2.zero;
            imageRect.anchorMax = Vector2.one;
            imageRect.offsetMin = Vector2.zero;
            imageRect.offsetMax = Vector2.zero;
            imageRect.SetSiblingIndex(1);

            var rawImage = instrumentImageObj.AddComponent<RawImage>();
            rawImage.color = Color.white;
            rawImage.raycastTarget = false;
            rawImage.enabled = false;

            var previewRenderer = instrumentImageObj.AddComponent<InstrumentPreviewRenderer>();
            var rendererSo = new SerializedObject(previewRenderer);
            rendererSo.FindProperty("display").objectReferenceValue = displayView;
            rendererSo.FindProperty("instrumentCamera").objectReferenceValue = projectionCamera;
            rendererSo.FindProperty("output").objectReferenceValue = rawImage;
            rendererSo.ApplyModifiedPropertiesWithoutUndo();
        }

        var calibration = displayRoot.AddComponent<CalibrationManager>();
        var controlBar = displayRoot.GetComponent<ScreenControlBar>();
        var presentationController = displayRoot.AddComponent<MainPresentationController>();
        var controllerSo = new SerializedObject(presentationController);
        controllerSo.FindProperty("controlBar").objectReferenceValue = controlBar;
        controllerSo.FindProperty("jangguObject").objectReferenceValue = janggu;
        controllerSo.FindProperty("bukObject").objectReferenceValue = buk;
        controllerSo.FindProperty("calibrationManager").objectReferenceValue = calibration;
        controllerSo.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void CreateTrackingSystem(Scene scene)
    {
        var trackingObj = new GameObject("TrackingSystem");
        SceneManager.MoveGameObjectToScene(trackingObj, scene);

        ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Tracking/Shaders/MalletTracker.compute");
        if (shader == null)
        {
            throw new InvalidOperationException("MalletTracker.compute is missing.");
        }

        var tracker = trackingObj.AddComponent<GpuMalletTracker>();
        tracker.trackerShader = shader;
        tracker.cameraId = "primary";
        tracker.cameraDeviceName = "";
        tracker.pixelMin = new Vector2(80f, 60f);
        tracker.pixelMax = new Vector2(1200f, 660f);
        tracker.areaMin = 100.0;
        tracker.areaMax = 3000.0;
        tracker.worldMin = new Vector3(-0.85f, -0.48f, 1.05f);
        tracker.worldMax = new Vector3(0.85f, 0.48f, 1.75f);
        tracker.enabled = false;

        var sessionController = trackingObj.AddComponent<TrackingSessionController>();
        var controllerSo = new SerializedObject(sessionController);
        controllerSo.FindProperty("tracker").objectReferenceValue = tracker;
        controllerSo.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void CreatePerformanceSystem(Scene scene, Transform jangguTransform, Transform bukTransform)
    {
        var perfObj = new GameObject("PerformanceSystem");
        SceneManager.MoveGameObjectToScene(perfObj, scene);

        var hitDetector = perfObj.AddComponent<InstrumentHitDetector>();
        var detectorSo = new SerializedObject(hitDetector);
        detectorSo.FindProperty("jangguRoot").objectReferenceValue = jangguTransform;
        detectorSo.FindProperty("bukRoot").objectReferenceValue = bukTransform;
        detectorSo.ApplyModifiedPropertiesWithoutUndo();

        AudioSource primarySource = CreateAudioSource(perfObj.transform, "PrimaryAudioSource");
        AudioSource secondarySource = CreateAudioSource(perfObj.transform, "SecondaryAudioSource");
        var soundProfile = AssetDatabase.LoadAssetAtPath<JangguSoundProfile>("Assets/Performance/Audio/JangguSoundProfile.asset");
        if (soundProfile == null)
        {
            throw new InvalidOperationException("JangguSoundProfile.asset is missing.");
        }

        var audioController = perfObj.AddComponent<InstrumentAudioController>();
        var audioSo = new SerializedObject(audioController);
        audioSo.FindProperty("jangguProfile").objectReferenceValue = soundProfile;
        audioSo.FindProperty("primarySource").objectReferenceValue = primarySource;
        audioSo.FindProperty("secondarySource").objectReferenceValue = secondarySource;
        audioSo.ApplyModifiedPropertiesWithoutUndo();

        perfObj.AddComponent<HardwareProfiler>();
    }

    private static AudioSource CreateAudioSource(Transform parent, string name)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var source = obj.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        return source;
    }

    private static void SetLayerRecursively(Transform root, int layer)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            child.gameObject.layer = layer;
        }
    }
}
