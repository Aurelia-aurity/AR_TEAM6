using UnityEngine;

/// <summary>
/// Work-scene-only tracking simulator. Publishes no samples during ordinary display.
/// Disable before connecting real tracking; it is not a calibration implementation.
/// </summary>
public sealed class MalletCursorPreviewSource : MonoBehaviour
{
    [SerializeField] private MalletCursorPresenter cursors;
    [SerializeField] private CameraDisplayView display;
    [SerializeField] private Camera testProjectionCamera;
    [SerializeField] private bool animate = true;
    [SerializeField] private bool leftTracked = true;
    [SerializeField] private bool rightTracked = true;
    [SerializeField] private Vector3 leftPosition = new Vector3(-1, 0.5f, 5);
    [SerializeField] private Vector3 rightPosition = new Vector3(1, -0.5f, 5);

    private bool wasPublishing;

    private void Update()
    {
        if (cursors == null || !cursors.isActiveAndEnabled || !cursors.DebugCursorsEnabled
            || display == null || !display.isActiveAndEnabled || display.Status != CameraDisplayStatus.Streaming
            || testProjectionCamera == null || cursors.ProjectionCamera != testProjectionCamera)
        {
            StopPublishing();
            return;
        }

        // Only the simulator's own camera is adjusted. Real calibration is externally supplied.
        testProjectionCamera.aspect = display.VideoAspectRatio;
        float time = Time.unscaledTime;
        Vector3 left = leftPosition;
        Vector3 right = rightPosition;
        if (animate)
        {
            left += new Vector3(0.5f * Mathf.Sin(time), 0.25f * Mathf.Cos(time), 0);
            right += new Vector3(0.5f * Mathf.Cos(time * 0.8f), 0.25f * Mathf.Sin(time * 0.8f), 0);
        }
        double timestampMs = Time.realtimeSinceStartupAsDouble * 1000.0;
        GameEventBus.OnMalletsTracked?.Invoke(
            new MalletState { MalletId = 0, Position = left, Confidence = leftTracked ? 1 : 0, TimestampMs = timestampMs },
            new MalletState { MalletId = 1, Position = right, Confidence = rightTracked ? 1 : 0, TimestampMs = timestampMs });
        if (!leftTracked) GameEventBus.PublishTrackingLost(0, timestampMs);
        if (!rightTracked) GameEventBus.PublishTrackingLost(1, timestampMs);
        wasPublishing = true;
    }

    private void OnDisable() => StopPublishing();

    private void StopPublishing()
    {
        if (!wasPublishing)
            return;
        double timestampMs = Time.realtimeSinceStartupAsDouble * 1000.0;
        GameEventBus.PublishTrackingLost(0, timestampMs);
        GameEventBus.PublishTrackingLost(1, timestampMs);
        wasPublishing = false;
    }
}
