using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Non-interactive HUD displaying per-mallet tracking health, tracking loss guidance (FR-015 / NFR-007),
/// and step-by-step calibration progress (FR-014).
/// </summary>
[DisallowMultipleComponent]
public sealed class TrackingStatusHud : MonoBehaviour
{
    private const double SampleTimeoutMs = 600.0;

    [SerializeField] private CalibrationManager calibrationManager;
    [SerializeField] private Font koreanFont;

    private struct MalletStatus
    {
        public bool Tracked;
        public bool HasLostNotice;
        public double LastTimestampMs;
        public double LastReceivedAtMs;
    }

    private MalletStatus left;
    private MalletStatus right;
    private bool sessionRunning;
    private string cameraErrorMessage;
    private CalibrationStep calibrationStep;
    private int calibrationSample;
    private int calibrationTarget = CalibrationManager.SamplesPerStep;
    private double calibrationCompletedUntilMs;

    private RectTransform safeArea;
    private Image leftPill;
    private Image rightPill;
    private Text leftLabel;
    private Text rightLabel;
    private GameObject bannerObject;
    private Text bannerLabel;
    private Font ownedFont;

    public bool IsLeftTracked => left.Tracked;
    public bool IsRightTracked => right.Tracked;
    public string StatusBannerText { get; private set; } = string.Empty;

    private void Awake()
    {
        EnsureUiBuilt();
        RefreshUi();
    }

    private void OnEnable()
    {
        EnsureUiBuilt();
        ResetTrackingStates();
        sessionRunning = GameEventBus.IsSessionRunning;

        GameEventBus.OnMalletsTracked += OnMalletsTracked;
        GameEventBus.OnTrackingLostAt += OnTrackingLostAt;
        GameEventBus.OnCameraInputStateChanged += OnCameraInputStateChanged;
        GameEventBus.OnSessionRunningChanged += OnSessionRunningChanged;
        if (calibrationManager != null)
        {
            calibrationManager.ProgressChanged += OnCalibrationProgressChanged;
            calibrationStep = calibrationManager.CurrentStep;
            calibrationSample = calibrationManager.CurrentSampleCount;
        }

        RefreshUi();
    }

    private void OnDisable()
    {
        GameEventBus.OnMalletsTracked -= OnMalletsTracked;
        GameEventBus.OnTrackingLostAt -= OnTrackingLostAt;
        GameEventBus.OnCameraInputStateChanged -= OnCameraInputStateChanged;
        GameEventBus.OnSessionRunningChanged -= OnSessionRunningChanged;
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

    public void SetCalibrationManager(CalibrationManager manager)
    {
        if (calibrationManager == manager)
        {
            return;
        }

        if (isActiveAndEnabled && calibrationManager != null)
        {
            calibrationManager.ProgressChanged -= OnCalibrationProgressChanged;
        }

        calibrationManager = manager;
        if (isActiveAndEnabled && calibrationManager != null)
        {
            calibrationManager.ProgressChanged += OnCalibrationProgressChanged;
            calibrationStep = calibrationManager.CurrentStep;
            calibrationSample = calibrationManager.CurrentSampleCount;
            RefreshUi();
        }
    }

    private void Update()
    {
        if (safeArea != null && Screen.width > 0 && Screen.height > 0)
        {
            Rect safe = Screen.safeArea;
            safeArea.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
            safeArea.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
        }

        double nowMs = Time.realtimeSinceStartupAsDouble * 1000.0;
        bool changed = false;
        if (left.Tracked && (nowMs - left.LastReceivedAtMs) > SampleTimeoutMs)
        {
            left.Tracked = false;
            left.HasLostNotice = true;
            changed = true;
        }

        if (right.Tracked && (nowMs - right.LastReceivedAtMs) > SampleTimeoutMs)
        {
            right.Tracked = false;
            right.HasLostNotice = true;
            changed = true;
        }

        if (calibrationStep == CalibrationStep.Completed && nowMs >= calibrationCompletedUntilMs)
        {
            calibrationStep = CalibrationStep.NotStarted;
            changed = true;
        }

        if (changed)
        {
            RefreshUi();
        }
    }

    private void OnSessionRunningChanged(bool running)
    {
        sessionRunning = running;
        if (!running)
        {
            ResetTrackingStates();
        }
        else
        {
            cameraErrorMessage = null;
        }

        RefreshUi();
    }

    private void OnCameraInputStateChanged(CameraInputStateData state)
    {
        if (!string.Equals(state.CameraId, "primary", StringComparison.Ordinal))
        {
            return;
        }

        if (state.State == CameraInputState.Error)
        {
            cameraErrorMessage = string.IsNullOrEmpty(state.Description)
                ? "카메라 연결을 확인해 주세요."
                : state.Description;
            ResetTrackingStates();
            RefreshUi();
        }
        else if (state.State == CameraInputState.Streaming || state.State == CameraInputState.Waiting)
        {
            if (cameraErrorMessage != null)
            {
                cameraErrorMessage = null;
                RefreshUi();
            }
        }
        else if (state.State == CameraInputState.Stopped)
        {
            ResetTrackingStates();
            RefreshUi();
        }
    }

    private void OnMalletsTracked(MalletState first, MalletState second)
    {
        double nowMs = Time.realtimeSinceStartupAsDouble * 1000.0;
        AcceptMalletState(first, nowMs);
        AcceptMalletState(second, nowMs);
        RefreshUi();
    }

    private void AcceptMalletState(MalletState state, double nowMs)
    {
        if (!GameEventBus.IsActiveContext(state.ContextVersion))
        {
            return;
        }

        if (double.IsNaN(state.TimestampMs) || double.IsInfinity(state.TimestampMs) || state.TimestampMs < 0.0)
        {
            return;
        }

        if (state.MalletId == 0)
        {
            UpdateMalletStatus(ref left, state, nowMs);
        }
        else if (state.MalletId == 1)
        {
            UpdateMalletStatus(ref right, state, nowMs);
        }
    }

    private static void UpdateMalletStatus(ref MalletStatus target, MalletState state, double nowMs)
    {
        if (state.TimestampMs < target.LastTimestampMs)
        {
            return;
        }

        target.LastTimestampMs = state.TimestampMs;
        bool valid = !float.IsNaN(state.Confidence)
            && !float.IsInfinity(state.Confidence)
            && state.Confidence > 0f
            && IsFinite(state.Position);
        target.Tracked = valid;
        if (valid)
        {
            target.LastReceivedAtMs = nowMs;
            target.HasLostNotice = false;
        }
        else
        {
            target.HasLostNotice = true;
        }
    }

    private void OnTrackingLostAt(int malletId, double timestampMs)
    {
        if (double.IsNaN(timestampMs) || double.IsInfinity(timestampMs) || timestampMs < 0.0)
        {
            return;
        }

        if (malletId == 0 && timestampMs >= left.LastTimestampMs)
        {
            left.LastTimestampMs = timestampMs;
            left.Tracked = false;
            left.HasLostNotice = true;
            RefreshUi();
        }
        else if (malletId == 1 && timestampMs >= right.LastTimestampMs)
        {
            right.LastTimestampMs = timestampMs;
            right.Tracked = false;
            right.HasLostNotice = true;
            RefreshUi();
        }
    }

    private void OnCalibrationProgressChanged(CalibrationStep step, int sampleCount, int totalSamples)
    {
        calibrationStep = step;
        calibrationSample = sampleCount;
        calibrationTarget = totalSamples > 0 ? totalSamples : CalibrationManager.SamplesPerStep;
        if (step == CalibrationStep.Completed)
        {
            calibrationCompletedUntilMs = Time.realtimeSinceStartupAsDouble * 1000.0 + 3000.0;
        }

        RefreshUi();
    }

    private void ResetTrackingStates()
    {
        left = new MalletStatus { LastTimestampMs = double.NegativeInfinity };
        right = new MalletStatus { LastTimestampMs = double.NegativeInfinity };
    }

    private void RefreshUi()
    {
        if (leftLabel == null)
        {
            return;
        }

        ApplyMalletPill(leftPill, leftLabel, "왼채", left);
        ApplyMalletPill(rightPill, rightLabel, "오른채", right);

        string banner = ResolveBannerText();
        StatusBannerText = banner;
        if (bannerObject != null)
        {
            bool showBanner = !string.IsNullOrEmpty(banner);
            if (bannerObject.activeSelf != showBanner)
            {
                bannerObject.SetActive(showBanner);
            }

            if (showBanner && bannerLabel != null)
            {
                bannerLabel.text = banner;
            }
        }
    }

    private static void ApplyMalletPill(Image pill, Text label, string prefix, MalletStatus status)
    {
        if (status.Tracked)
        {
            pill.color = new Color32(28, 96, 86, 232);
            label.text = $"{prefix}: 추적 정상";
        }
        else if (status.HasLostNotice)
        {
            pill.color = new Color32(130, 56, 56, 232);
            label.text = $"{prefix}: 미검출";
        }
        else
        {
            pill.color = new Color32(34, 46, 62, 220);
            label.text = $"{prefix}: 대기";
        }
    }

    private string ResolveBannerText()
    {
        if (calibrationStep > CalibrationStep.NotStarted && calibrationStep <= CalibrationStep.Completed)
        {
            return FormatCalibrationPrompt(calibrationStep, calibrationSample, calibrationTarget);
        }

        if (!string.IsNullOrEmpty(cameraErrorMessage))
        {
            return $"카메라 오류: {cameraErrorMessage}";
        }

        if (sessionRunning && (left.HasLostNotice || right.HasLostNotice) && (!left.Tracked || !right.Tracked))
        {
            if (!left.Tracked && !right.Tracked)
            {
                return "양쪽 채가 인식되지 않습니다. 카메라 시야 안으로 채를 가져오세요.";
            }

            return !left.Tracked
                ? "왼채 추적이 끊겼습니다. 오른채로 계속 연주하거나 왼채를 카메라에 비춰주세요."
                : "오른채 추적이 끊겼습니다. 왼채로 계속 연주하거나 오른채를 카메라에 비춰주세요.";
        }

        return string.Empty;
    }

    private static string FormatCalibrationPrompt(CalibrationStep step, int sampleCount, int totalSamples)
    {
        switch (step)
        {
            case CalibrationStep.TopLeft:
                return $"보정 1/5 (왼쪽 위): 왼쪽 채(L)를 화면 왼쪽 위 모서리에 유지 ({sampleCount}/{totalSamples})";
            case CalibrationStep.TopRight:
                return $"보정 2/5 (오른쪽 위): 왼쪽 채(L)를 화면 오른쪽 위 모서리에 유지 ({sampleCount}/{totalSamples})";
            case CalibrationStep.BottomLeft:
                return $"보정 3/5 (왼쪽 아래): 왼쪽 채(L)를 화면 왼쪽 아래 모서리에 유지 ({sampleCount}/{totalSamples})";
            case CalibrationStep.BottomRight:
                return $"보정 4/5 (오른쪽 아래): 왼쪽 채(L)를 화면 오른쪽 아래 모서리에 유지 ({sampleCount}/{totalSamples})";
            case CalibrationStep.RestOnDesk:
                return $"보정 5/5 (기준 깊이): 왼쪽 채(L)를 연주 기준 위치에 정지 유지 ({sampleCount}/{totalSamples})";
            case CalibrationStep.Completed:
                return "보정이 완료되어 추적 좌표에 적용되었습니다.";
            default:
                return string.Empty;
        }
    }

    private void EnsureUiBuilt()
    {
        if (safeArea != null)
        {
            return;
        }

        if (koreanFont == null)
        {
            ownedFont = Font.CreateDynamicFontFromOSFont(
                new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Arial" }, 14);
            koreanFont = ownedFont;
        }

        var canvasRect = CreateRect("TrackingStatusCanvas", transform);
        var canvas = canvasRect.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 95;
        var scaler = canvasRect.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

        safeArea = CreateRect("SafeArea", canvasRect);
        safeArea.anchorMin = Vector2.zero;
        safeArea.anchorMax = Vector2.one;
        safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;

        CreatePill("LeftMalletPill", safeArea, new Vector2(-180, -16), out leftPill, out leftLabel);
        CreatePill("RightMalletPill", safeArea, new Vector2(-16, -16), out rightPill, out rightLabel);

        RectTransform bannerRect = CreateRect("GuidanceBanner", safeArea);
        bannerRect.anchorMin = new Vector2(0.5f, 1f);
        bannerRect.anchorMax = new Vector2(0.5f, 1f);
        bannerRect.pivot = new Vector2(0.5f, 1f);
        bannerRect.anchoredPosition = new Vector2(0, -56);
        bannerRect.sizeDelta = new Vector2(640, 36);
        Image bannerBg = bannerRect.gameObject.AddComponent<Image>();
        bannerBg.color = new Color32(20, 32, 48, 232);
        bannerBg.raycastTarget = false;

        RectTransform bannerTextRect = CreateRect("Label", bannerRect);
        bannerTextRect.anchorMin = Vector2.zero;
        bannerTextRect.anchorMax = Vector2.one;
        bannerTextRect.offsetMin = new Vector2(12, 4);
        bannerTextRect.offsetMax = new Vector2(-12, -4);
        bannerLabel = bannerTextRect.gameObject.AddComponent<Text>();
        bannerLabel.font = koreanFont;
        bannerLabel.fontSize = 14;
        bannerLabel.color = new Color32(238, 244, 250, 255);
        bannerLabel.alignment = TextAnchor.MiddleCenter;
        bannerLabel.raycastTarget = false;

        bannerObject = bannerRect.gameObject;
        bannerObject.SetActive(false);
    }

    private void CreatePill(string name, Transform parent, Vector2 topRightOffset, out Image background, out Text label)
    {
        RectTransform pillRect = CreateRect(name, parent);
        pillRect.anchorMin = new Vector2(1f, 1f);
        pillRect.anchorMax = new Vector2(1f, 1f);
        pillRect.pivot = new Vector2(1f, 1f);
        pillRect.anchoredPosition = topRightOffset;
        pillRect.sizeDelta = new Vector2(152, 30);
        background = pillRect.gameObject.AddComponent<Image>();
        background.color = new Color32(34, 46, 62, 220);
        background.raycastTarget = false;

        RectTransform textRect = CreateRect("Label", pillRect);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(8, 2);
        textRect.offsetMax = new Vector2(-8, -2);
        label = textRect.gameObject.AddComponent<Text>();
        label.font = koreanFont;
        label.fontSize = 13;
        label.color = new Color32(236, 242, 248, 255);
        label.alignment = TextAnchor.MiddleCenter;
        label.raycastTarget = false;
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false);
        return rect;
    }

    private static bool IsFinite(Vector3 value) =>
        !float.IsNaN(value.x) && !float.IsInfinity(value.x)
        && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
        && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
}
