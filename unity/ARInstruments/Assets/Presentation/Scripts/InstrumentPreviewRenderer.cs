using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Renders a dedicated world-space instrument camera into the display overlay.
/// MirrorRoot mirrors the resulting image; no world pose or tracking data changes.
/// This preview camera is not a substitute for camera calibration.
/// </summary>
[DisallowMultipleComponent]
public sealed class InstrumentPreviewRenderer : MonoBehaviour
{
    [SerializeField] private CameraDisplayView display;
    [SerializeField] private Camera instrumentCamera;
    [SerializeField] private RawImage output;

    private RenderTexture target;
    public Camera InstrumentCamera => instrumentCamera;
    public RenderTexture OutputTexture => target;

    private void Awake()
    {
        if (display == null || instrumentCamera == null || output == null)
        {
            Debug.LogError("InstrumentPreviewRenderer requires a display, dedicated camera and output image.", this);
            enabled = false;
            return;
        }
        instrumentCamera.enabled = false;
        output.raycastTarget = false;
    }

    private void OnEnable()
    {
        if (display != null) display.InputInvalidated += HideOutput;
    }

    private void LateUpdate()
    {
        if (display == null || !display.isActiveAndEnabled || display.Status != CameraDisplayStatus.Streaming)
        {
            HideOutput();
            return;
        }

        float aspect = display.VideoAspectRatio;
        // Bound allocation size while preserving portrait and landscape aspect.
        int height = Mathf.RoundToInt(Mathf.Min(720, 1280 / aspect));
        int width = Mathf.RoundToInt(height * aspect);
        width = Mathf.Max(1, width);
        height = Mathf.Max(1, height);
        if (target == null || target.width != width || target.height != height)
        {
            ReleaseTarget();
            target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "Instrument preview (owned)",
                antiAliasing = 1,
                filterMode = FilterMode.Bilinear
            };
            target.Create();
            instrumentCamera.targetTexture = target;
            output.texture = target;
        }

        instrumentCamera.aspect = aspect;
        instrumentCamera.Render();
        output.enabled = true;
    }

    private void HideOutput()
    {
        if (output != null) output.enabled = false;
    }

    private void OnDisable()
    {
        if (display != null) display.InputInvalidated -= HideOutput;
        HideOutput();
        ReleaseTarget();
    }

    private void ReleaseTarget()
    {
        if (target == null) return;
        if (output != null && output.texture == target) output.texture = null;
        if (instrumentCamera != null && instrumentCamera.targetTexture == target)
            instrumentCamera.targetTexture = null;
        target.Release();
        if (Application.isPlaying) Destroy(target);
        else DestroyImmediate(target);
        target = null;
    }
}
