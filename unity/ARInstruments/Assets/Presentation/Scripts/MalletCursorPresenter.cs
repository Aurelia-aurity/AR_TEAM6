using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Debug/calibration cursors for the shared world-space tracking events.
/// The supplied camera must project into the orientation-corrected reference image.
/// MirrorRoot handles display mirroring; source positions are never modified.
/// </summary>
[DisallowMultipleComponent]
public sealed class MalletCursorPresenter : MonoBehaviour
{
    [SerializeField] private CameraDisplayView display;
    [SerializeField] private Camera projectionCamera;
    [SerializeField] private RectTransform leftCursor;
    [SerializeField] private RectTransform rightCursor;
    [SerializeField] private Toggle debugToggle;
    [SerializeField, Min(0.05f)] private float maxSampleAgeSeconds = 0.5f;

    private struct CursorSample
    {
        public MalletState State;
        public double ReceivedAtMs;
        public double LastTimestampMs;
        public bool Tracked;
    }

    private CursorSample left;
    private CursorSample right;
    private bool debugEnabled;

    public bool DebugCursorsEnabled => debugEnabled;
    public Camera ProjectionCamera => projectionCamera;

    private void Awake()
    {
        if (display == null || leftCursor == null || rightCursor == null || debugToggle == null)
        {
            Debug.LogError("MalletCursorPresenter requires its display, cursors, and debug Toggle.", this);
            enabled = false;
        }
    }

    private void OnEnable()
    {
        ResetSamples();
        GameEventBus.OnMalletsTracked += OnMalletsTracked;
        GameEventBus.OnTrackingLost += OnTrackingLost;
        if (debugToggle != null)
        {
            debugToggle.onValueChanged.AddListener(SetDebugCursorsEnabled);
            debugToggle.interactable = true;
            debugToggle.SetIsOnWithoutNotify(debugEnabled);
        }
    }

    private void OnDisable()
    {
        GameEventBus.OnMalletsTracked -= OnMalletsTracked;
        GameEventBus.OnTrackingLost -= OnTrackingLost;
        if (debugToggle != null)
        {
            debugToggle.onValueChanged.RemoveListener(SetDebugCursorsEnabled);
            debugToggle.interactable = false;
        }
        ResetSamples();
        HideCursors();
    }

    public void SetDebugCursorsEnabled(bool value)
    {
        if (debugToggle != null)
            debugToggle.SetIsOnWithoutNotify(value);
        if (debugEnabled == value)
            return;
        debugEnabled = value;
        // Never restore samples left over from a previous debug session.
        ResetSamples();
        HideCursors();
    }

    public void SetProjectionCamera(Camera value)
    {
        if (projectionCamera == value)
            return;
        projectionCamera = value;
        ResetSamples();
        HideCursors();
    }

    private void LateUpdate()
    {
        if (!debugEnabled || display == null || !display.isActiveAndEnabled
            || display.Status != CameraDisplayStatus.Streaming || projectionCamera == null)
        {
            ResetSamples();
            HideCursors();
            return;
        }

        double nowMs = Time.realtimeSinceStartupAsDouble * 1000.0;
        RefreshCursor(leftCursor, ref left, nowMs);
        RefreshCursor(rightCursor, ref right, nowMs);
    }

    private void OnMalletsTracked(MalletState first, MalletState second)
    {
        if (!debugEnabled)
            return;
        Accept(first);
        Accept(second);
    }

    private void Accept(MalletState state)
    {
        if (state.MalletId == 0)
            Accept(ref left, state);
        else if (state.MalletId == 1)
            Accept(ref right, state);
    }

    private static void Accept(ref CursorSample sample, MalletState state)
    {
        double nowMs = Time.realtimeSinceStartupAsDouble * 1000.0;
        if (double.IsNaN(state.TimestampMs) || double.IsInfinity(state.TimestampMs) || state.TimestampMs < 0
            || state.TimestampMs > nowMs + 50.0)
        {
            sample.Tracked = false;
            return;
        }
        if (state.TimestampMs <= sample.LastTimestampMs)
            return;

        sample.LastTimestampMs = state.TimestampMs;
        sample.State = state;
        sample.ReceivedAtMs = nowMs;
        sample.Tracked = IsFinite(state.Confidence) && state.Confidence > 0
            && IsFinite(state.Position.x) && IsFinite(state.Position.y) && IsFinite(state.Position.z);
    }

    private void OnTrackingLost(int malletId)
    {
        if (malletId == 0)
        {
            left.Tracked = false;
            SetVisible(leftCursor, false);
        }
        else if (malletId == 1)
        {
            right.Tracked = false;
            SetVisible(rightCursor, false);
        }
    }

    private void RefreshCursor(RectTransform cursor, ref CursorSample sample, double nowMs)
    {
        double maxAgeMs = Mathf.Max(0.05f, maxSampleAgeSeconds) * 1000.0;
        double captureAgeMs = nowMs - sample.State.TimestampMs;
        if (!sample.Tracked || nowMs - sample.ReceivedAtMs > maxAgeMs
            || captureAgeMs > maxAgeMs || captureAgeMs < -50.0)
        {
            sample.Tracked = false;
            SetVisible(cursor, false);
            return;
        }

        Vector3 point = projectionCamera.WorldToViewportPoint(sample.State.Position);
        bool visible = IsFinite(point.x) && IsFinite(point.y) && IsFinite(point.z)
            && point.z >= projectionCamera.nearClipPlane && point.z <= projectionCamera.farClipPlane
            && point.x >= 0 && point.x <= 1 && point.y >= 0 && point.y <= 1;
        if (visible && cursor != null)
        {
            cursor.anchorMin = cursor.anchorMax = new Vector2(point.x, point.y);
            cursor.anchoredPosition = Vector2.zero;
        }
        SetVisible(cursor, visible);
    }

    private void ResetSamples()
    {
        left = new CursorSample { LastTimestampMs = double.NegativeInfinity };
        right = new CursorSample { LastTimestampMs = double.NegativeInfinity };
    }

    private void HideCursors()
    {
        SetVisible(leftCursor, false);
        SetVisible(rightCursor, false);
    }

    private static void SetVisible(RectTransform cursor, bool value)
    {
        if (cursor != null && cursor.gameObject.activeSelf != value)
            cursor.gameObject.SetActive(value);
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
