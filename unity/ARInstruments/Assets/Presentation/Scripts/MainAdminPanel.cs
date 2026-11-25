using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Runtime Administrator drawer for Main.unity providing camera device selection,
/// calibration triggers (FR-014), active instrument 3D pose tuning (FR-003),
/// and a one-click simultaneous strike diagnostic trigger (FR-011).
/// </summary>
[DisallowMultipleComponent]
public sealed class MainAdminPanel : MonoBehaviour
{
    private const string CameraPrefKey = "ARInstruments.CameraDeviceName";

    [SerializeField] private Transform jangguTransform;
    [SerializeField] private Transform bukTransform;
    [SerializeField] private CalibrationManager calibrationManager;
    [SerializeField] private Font koreanFont;

    private struct SavedPose
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
        public bool Initialized;
    }

    private SavedPose jangguDefaultPose;
    private SavedPose bukDefaultPose;
    private ScreenInstrument activeInstrument = ScreenInstrument.Janggu;
    private bool panelVisible;
    private string selectedCameraDevice = string.Empty;
    private double lastDiagnosticTimestampMs;

    private GameObject panelRoot;
    private Text cameraButtonLabel;
    private Text calibrationButtonLabel;
    private Text calibrationStatusLabel;
    private Slider posXSlider;
    private Slider posYSlider;
    private Slider posZSlider;
    private Slider rotYSlider;
    private Slider scaleSlider;
    private Text posXValue;
    private Text posYValue;
    private Text posZValue;
    private Text rotYValue;
    private Text scaleValue;
    private Font ownedFont;

    public bool IsPanelVisible => panelVisible;

    private Transform ActiveInstrumentTransform =>
        activeInstrument == ScreenInstrument.Buk ? bukTransform : jangguTransform;

    private void Awake()
    {
        CaptureDefaultPoses();
        selectedCameraDevice = PlayerPrefs.GetString(CameraPrefKey, string.Empty);
        EnsureUiBuilt();
        panelRoot.SetActive(panelVisible);
        SyncPoseSlidersFromTransform();
        RefreshCameraButtonLabel();
        RefreshCalibrationControls();
    }

    private void OnEnable()
    {
        EnsureUiBuilt();
        if (!string.IsNullOrEmpty(selectedCameraDevice))
        {
            GameEventBus.PublishCameraDeviceSelected(selectedCameraDevice);
        }

        if (calibrationManager != null)
        {
            calibrationManager.ProgressChanged += OnCalibrationProgressChanged;
        }

        RefreshCameraButtonLabel();
        RefreshCalibrationControls();
    }

    private void OnDisable()
    {
        if (calibrationManager != null)
        {
            calibrationManager.ProgressChanged -= OnCalibrationProgressChanged;
        }
    }

    private void OnDestroy()
    {
        if (ownedFont != null)
        {
            Destroy(ownedFont);
            ownedFont = null;
        }
    }

    public void ConfigureTargets(Transform janggu, Transform buk, CalibrationManager calibration)
    {
        jangguTransform = janggu;
        bukTransform = buk;
        if (isActiveAndEnabled && calibrationManager != null)
        {
            calibrationManager.ProgressChanged -= OnCalibrationProgressChanged;
        }

        calibrationManager = calibration;
        if (isActiveAndEnabled && calibrationManager != null)
        {
            calibrationManager.ProgressChanged += OnCalibrationProgressChanged;
        }

        CaptureDefaultPoses();
        SyncPoseSlidersFromTransform();
        RefreshCalibrationControls();
    }

    public void SetVisible(bool visible)
    {
        panelVisible = visible;
        EnsureUiBuilt();
        if (panelRoot != null)
        {
            panelRoot.SetActive(visible);
        }

        if (visible)
        {
            SyncPoseSlidersFromTransform();
            RefreshCameraButtonLabel();
            RefreshCalibrationControls();
        }
    }

    public void SetActiveInstrument(ScreenInstrument instrument)
    {
        activeInstrument = instrument;
        SyncPoseSlidersFromTransform();
    }

    public void ApplyPose(Vector3 position, float rotationY, float scale)
    {
        Transform target = ActiveInstrumentTransform;
        if (target == null)
        {
            return;
        }

        CaptureDefaultPoses();
        SavedPose defaults = activeInstrument == ScreenInstrument.Buk ? bukDefaultPose : jangguDefaultPose;
        Vector3 defaultAngles = defaults.Initialized ? defaults.Rotation.eulerAngles : target.rotation.eulerAngles;
        Vector3 baseScale = defaults.Initialized ? defaults.Scale : Vector3.one;

        target.SetPositionAndRotation(position, Quaternion.Euler(defaultAngles.x, rotationY, defaultAngles.z));
        target.localScale = baseScale * Mathf.Clamp(scale, 0.4f, 2.0f);
        SyncPoseSlidersFromTransform();
    }

    public void ResetActiveInstrumentPose()
    {
        Transform target = ActiveInstrumentTransform;
        if (target == null)
        {
            return;
        }

        SavedPose defaults = activeInstrument == ScreenInstrument.Buk ? bukDefaultPose : jangguDefaultPose;
        if (!defaults.Initialized)
        {
            return;
        }

        target.SetPositionAndRotation(defaults.Position, defaults.Rotation);
        target.localScale = defaults.Scale;
        SyncPoseSlidersFromTransform();
    }

    public void CycleCameraDevice()
    {
        WebCamDevice[] devices = WebCamTexture.devices;
        var options = new List<string> { string.Empty };
        if (devices != null)
        {
            for (int i = 0; i < devices.Length; i++)
            {
                if (!string.IsNullOrEmpty(devices[i].name))
                {
                    options.Add(devices[i].name);
                }
            }
        }

        int currentIndex = options.FindIndex(name => string.Equals(name, selectedCameraDevice, StringComparison.Ordinal));
        int nextIndex = currentIndex >= 0 ? (currentIndex + 1) % options.Count : 0;
        selectedCameraDevice = options[nextIndex];
        PlayerPrefs.SetString(CameraPrefKey, selectedCameraDevice);
        PlayerPrefs.Save();
        GameEventBus.PublishCameraDeviceSelected(selectedCameraDevice);
        RefreshCameraButtonLabel();
    }

    public void ToggleCalibration()
    {
        if (calibrationManager == null)
        {
            return;
        }

        if (calibrationManager.IsCalibrating)
        {
            calibrationManager.Cancel();
        }
        else
        {
            calibrationManager.Begin();
        }

        RefreshCalibrationControls();
    }

    public void TriggerDiagnosticDualStrike()
    {
        Transform target = ActiveInstrumentTransform;
        if (target == null)
        {
            return;
        }

        Transform headA = FindChildByName(target, "Head_A_Inner");
        Transform headB = FindChildByName(target, "Head_B_Inner");
        if (headA == null || headB == null)
        {
            return;
        }

        MeshFilter meshA = headA.GetComponent<MeshFilter>();
        MeshFilter meshB = headB.GetComponent<MeshFilter>();
        if (meshA == null || meshA.sharedMesh == null || meshB == null || meshB.sharedMesh == null)
        {
            return;
        }

        double nowMs = Time.realtimeSinceStartupAsDouble * 1000.0;
        double t0 = Math.Max(nowMs, lastDiagnosticTimestampMs + 30.0);
        double t1 = t0 + 20.0;
        lastDiagnosticTimestampMs = t1;

        Bounds boundsA = meshA.sharedMesh.bounds;
        Vector3 centerA = headA.TransformPoint(new Vector3(boundsA.max.x, boundsA.center.y, boundsA.center.z));
        Vector3 outwardA = headA.TransformDirection(Vector3.right).normalized;

        Bounds boundsB = meshB.sharedMesh.bounds;
        Vector3 centerB = headB.TransformPoint(new Vector3(boundsB.min.x, boundsB.center.y, boundsB.center.z));
        Vector3 outwardB = headB.TransformDirection(Vector3.left).normalized;

        long version = GameEventBus.ContextVersion;
        GameEventBus.OnMalletsTracked?.Invoke(
            new MalletState
            {
                MalletId = 0,
                Position = centerB + outwardB * 0.25f,
                Confidence = 1f,
                TimestampMs = t0,
                ContextVersion = version
            },
            new MalletState
            {
                MalletId = 1,
                Position = centerA + outwardA * 0.25f,
                Confidence = 1f,
                TimestampMs = t0,
                ContextVersion = version
            });
        GameEventBus.OnMalletsTracked?.Invoke(
            new MalletState
            {
                MalletId = 0,
                Position = centerB - outwardB * 0.08f,
                Confidence = 1f,
                TimestampMs = t1,
                ContextVersion = version
            },
            new MalletState
            {
                MalletId = 1,
                Position = centerA - outwardA * 0.08f,
                Confidence = 1f,
                TimestampMs = t1,
                ContextVersion = version
            });
    }

    private void CaptureDefaultPoses()
    {
        if (!jangguDefaultPose.Initialized && jangguTransform != null)
        {
            jangguDefaultPose = new SavedPose
            {
                Position = jangguTransform.position,
                Rotation = jangguTransform.rotation,
                Scale = jangguTransform.localScale,
                Initialized = true
            };
        }

        if (!bukDefaultPose.Initialized && bukTransform != null)
        {
            bukDefaultPose = new SavedPose
            {
                Position = bukTransform.position,
                Rotation = bukTransform.rotation,
                Scale = bukTransform.localScale,
                Initialized = true
            };
        }
    }

    private void OnCalibrationProgressChanged(CalibrationStep step, int sampleCount, int totalSamples)
    {
        RefreshCalibrationControls();
    }

    private void RefreshCameraButtonLabel()
    {
        if (cameraButtonLabel != null)
        {
            cameraButtonLabel.text = string.IsNullOrEmpty(selectedCameraDevice)
                ? "카메라: 자동 선택"
                : $"카메라: {selectedCameraDevice}";
        }
    }

    private void RefreshCalibrationControls()
    {
        if (calibrationButtonLabel == null || calibrationStatusLabel == null)
        {
            return;
        }

        if (calibrationManager == null)
        {
            calibrationButtonLabel.text = "보정 불가";
            calibrationStatusLabel.text = "보정 매니저 없음";
            return;
        }

        if (calibrationManager.IsCalibrating)
        {
            calibrationButtonLabel.text = "보정 취소";
            calibrationStatusLabel.text = $"진행 중: {(int)calibrationManager.CurrentStep}/5 ({calibrationManager.CurrentSampleCount}/{CalibrationManager.SamplesPerStep})";
        }
        else if (calibrationManager.CurrentStep == CalibrationStep.Completed)
        {
            calibrationButtonLabel.text = "다시 보정";
            calibrationStatusLabel.text = "상태: 보정 완료";
        }
        else
        {
            calibrationButtonLabel.text = "보정 시작";
            calibrationStatusLabel.text = "상태: 기본 보정 사용 중";
        }
    }

    private void OnSlidersChanged(float _)
    {
        if (posXSlider == null)
        {
            return;
        }

        ApplyPose(
            new Vector3(posXSlider.value, posYSlider.value, posZSlider.value),
            rotYSlider.value,
            scaleSlider.value);
    }

    private void SyncPoseSlidersFromTransform()
    {
        if (posXSlider == null)
        {
            return;
        }

        Transform target = ActiveInstrumentTransform;
        if (target == null)
        {
            return;
        }

        SavedPose defaults = activeInstrument == ScreenInstrument.Buk ? bukDefaultPose : jangguDefaultPose;
        float baseScale = defaults.Initialized && defaults.Scale.x > 0.001f ? defaults.Scale.x : 1f;
        float scaleRatio = target.localScale.x / baseScale;
        float rotY = Mathf.DeltaAngle(0f, target.rotation.eulerAngles.y);

        posXSlider.SetValueWithoutNotify(target.position.x);
        posYSlider.SetValueWithoutNotify(target.position.y);
        posZSlider.SetValueWithoutNotify(target.position.z);
        rotYSlider.SetValueWithoutNotify(rotY);
        scaleSlider.SetValueWithoutNotify(scaleRatio);

        posXValue.text = $"{posXSlider.value:F2} m";
        posYValue.text = $"{posYSlider.value:F2} m";
        posZValue.text = $"{posZSlider.value:F2} m";
        rotYValue.text = $"{rotYSlider.value:F0} deg";
        scaleValue.text = $"{scaleSlider.value:F2}x";
    }

    private void EnsureUiBuilt()
    {
        if (panelRoot != null)
        {
            return;
        }

        if (koreanFont == null)
        {
            ownedFont = Font.CreateDynamicFontFromOSFont(
                new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Arial" }, 13);
            koreanFont = ownedFont;
        }

        RectTransform canvasRect = CreateRect("AdminPanelCanvas", transform);
        var canvas = canvasRect.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 98;
        var scaler = canvasRect.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        canvasRect.gameObject.AddComponent<GraphicRaycaster>();

        RectTransform panel = CreateRect("AdminPanel", canvasRect);
        panel.anchorMin = new Vector2(0f, 0f);
        panel.anchorMax = new Vector2(0f, 1f);
        panel.pivot = new Vector2(0f, 0.5f);
        panel.offsetMin = new Vector2(16f, 108f);
        panel.offsetMax = new Vector2(306f, -64f);
        panel.gameObject.AddComponent<Image>().color = new Color32(18, 28, 41, 236);
        panelRoot = panel.gameObject;

        MakeLabel("Header", panel, "관리자 설정 (Admin)", new Vector2(14, -14), new Vector2(260, 22), 15, FontStyle.Bold);

        Button cameraBtn = MakeButton("CameraDeviceButton", panel, "카메라: 자동 선택",
            new Vector2(14, -44), new Vector2(262, 30), out cameraButtonLabel);
        cameraBtn.onClick.AddListener(CycleCameraDevice);

        Button calibBtn = MakeButton("CalibrationButton", panel, "보정 시작",
            new Vector2(14, -82), new Vector2(106, 30), out calibrationButtonLabel);
        calibBtn.image.color = new Color32(41, 98, 109, 255);
        calibBtn.onClick.AddListener(ToggleCalibration);

        calibrationStatusLabel = MakeLabel("CalibrationStatus", panel, "상태: 기본 보정 사용 중",
            new Vector2(128, -82), new Vector2(148, 30), 12, FontStyle.Normal);
        calibrationStatusLabel.alignment = TextAnchor.MiddleLeft;

        MakeLabel("PoseHeader", panel, "악기 3D 배치 (m / deg)", new Vector2(14, -122), new Vector2(260, 20), 13, FontStyle.Bold);
        posXSlider = MakeSlider(panel, "PosX", "위치 X", -146f, -0.6f, 0.6f, 0f, out posXValue);
        posYSlider = MakeSlider(panel, "PosY", "위치 Y", -186f, -0.5f, 0.5f, -0.06f, out posYValue);
        posZSlider = MakeSlider(panel, "PosZ", "위치 Z", -226f, 0.7f, 2.4f, 1.4f, out posZValue);
        rotYSlider = MakeSlider(panel, "RotY", "회전 Y", -266f, -180f, 180f, 0f, out rotYValue);
        scaleSlider = MakeSlider(panel, "Scale", "크기", -306f, 0.5f, 1.8f, 1f, out scaleValue);

        Button resetPoseBtn = MakeButton("ResetPoseButton", panel, "배치 초기화",
            new Vector2(14, -356), new Vector2(118, 30), out _);
        resetPoseBtn.onClick.AddListener(ResetActiveInstrumentPose);

        Button testStrikeBtn = MakeButton("TestStrikeButton", panel, "테스트 동시 타격",
            new Vector2(140, -356), new Vector2(136, 30), out _);
        testStrikeBtn.image.color = new Color32(34, 113, 110, 255);
        testStrikeBtn.onClick.AddListener(TriggerDiagnosticDualStrike);
    }

    private Slider MakeSlider(Transform parent, string name, string title, float yOffset, float min, float max, float initial, out Text valueLabel)
    {
        RectTransform row = CreateRect(name, parent);
        row.anchorMin = new Vector2(0f, 1f);
        row.anchorMax = new Vector2(1f, 1f);
        row.pivot = new Vector2(0.5f, 1f);
        row.anchoredPosition = new Vector2(0f, yOffset);
        row.sizeDelta = new Vector2(-28f, 36f);

        MakeLabel("Label", row, title, Vector2.zero, new Vector2(140, 18), 12, FontStyle.Normal);
        valueLabel = MakeLabel("Value", row, initial.ToString("F2"), new Vector2(140, 0), new Vector2(122, 18), 12, FontStyle.Normal);
        valueLabel.alignment = TextAnchor.UpperRight;

        RectTransform track = CreateRect("Slider", row);
        track.anchorMin = new Vector2(0f, 1f);
        track.anchorMax = new Vector2(1f, 1f);
        track.pivot = new Vector2(0.5f, 1f);
        track.anchoredPosition = new Vector2(0f, -22f);
        track.sizeDelta = new Vector2(0f, 12f);
        track.gameObject.AddComponent<Image>().color = new Color32(55, 69, 86, 255);

        Slider slider = track.gameObject.AddComponent<Slider>();
        RectTransform fill = CreateRect("Fill", track);
        Stretch(fill);
        Image fillImg = fill.gameObject.AddComponent<Image>();
        fillImg.color = new Color32(78, 173, 167, 255);
        fillImg.raycastTarget = false;

        RectTransform handleArea = CreateRect("HandleArea", track);
        Stretch(handleArea);
        handleArea.offsetMin = new Vector2(6f, 0f);
        handleArea.offsetMax = new Vector2(-6f, 0f);
        RectTransform handle = CreateRect("Handle", handleArea);
        handle.sizeDelta = new Vector2(12f, 18f);
        Image handleImg = handle.gameObject.AddComponent<Image>();
        handleImg.color = new Color32(233, 240, 247, 255);

        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.targetGraphic = handleImg;
        slider.minValue = min;
        slider.maxValue = max;
        slider.SetValueWithoutNotify(initial);
        slider.onValueChanged.AddListener(OnSlidersChanged);
        return slider;
    }

    private Button MakeButton(string name, Transform parent, string title, Vector2 topLeftOffset, Vector2 size, out Text label)
    {
        RectTransform rect = CreateRect(name, parent);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = topLeftOffset;
        rect.sizeDelta = size;
        Image img = rect.gameObject.AddComponent<Image>();
        img.color = new Color32(42, 57, 76, 255);
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = img;

        RectTransform textRect = CreateRect("Label", rect);
        Stretch(textRect);
        label = textRect.gameObject.AddComponent<Text>();
        label.font = koreanFont;
        label.fontSize = 12;
        label.color = new Color32(236, 242, 248, 255);
        label.alignment = TextAnchor.MiddleCenter;
        label.text = title;
        label.raycastTarget = false;
        return button;
    }

    private Text MakeLabel(string name, Transform parent, string textValue, Vector2 topLeftOffset, Vector2 size, int fontSize, FontStyle style)
    {
        RectTransform rect = CreateRect(name, parent);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = topLeftOffset;
        rect.sizeDelta = size;
        Text label = rect.gameObject.AddComponent<Text>();
        label.font = koreanFont;
        label.fontSize = fontSize;
        label.fontStyle = style;
        label.color = new Color32(233, 240, 247, 255);
        label.text = textValue;
        label.raycastTarget = false;
        return label;
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false);
        return rect;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static Transform FindChildByName(Transform root, string targetName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == targetName)
            {
                return child;
            }
        }

        return null;
    }
}
