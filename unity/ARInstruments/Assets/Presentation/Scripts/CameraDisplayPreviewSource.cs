using UnityEngine;

/// <summary>
/// Optional input simulator for the B work scene. Never captures from a device.
/// Disable this component before connecting a real input provider to the view.
/// </summary>
[DisallowMultipleComponent]
public sealed class CameraDisplayPreviewSource : MonoBehaviour
{
    public enum InputMode : byte { TestPattern, Waiting, SimulatedError }
    public enum Rotation : ushort { None = 0, Clockwise90 = 90, Clockwise180 = 180, Clockwise270 = 270 }

    [SerializeField] private CameraDisplayView display;
    [SerializeField] private InputMode inputMode;
    [SerializeField] private Vector2Int frameSize = new Vector2Int(640, 360);
    [SerializeField] private Rotation clockwiseRotation;
    [SerializeField] private bool verticallyMirrored;

    private Texture2D pattern;

    private void Update()
    {
        if (display == null || !display.isActiveAndEnabled)
            return;

        switch (inputMode)
        {
            case InputMode.Waiting:
                display.ShowWaiting();
                break;
            case InputMode.SimulatedError:
                display.ShowError("Test input is unavailable.");
                break;
            default:
                EnsurePattern();
                display.PresentFrame(pattern, (int)clockwiseRotation, verticallyMirrored, "Test pattern preview");
                break;
        }
    }

    private void EnsurePattern()
    {
        int width = Mathf.Clamp(frameSize.x, 32, 2048);
        int height = Mathf.Clamp(frameSize.y, 32, 2048);
        if (pattern != null && pattern.width == width && pattern.height == height)
            return;

        ReleasePattern();
        pattern = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "Camera display test pattern",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        Color32[] pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // UV origin is bottom-left: top red/green, bottom blue/yellow.
                Color color = y >= height / 2
                    ? (x < width / 2 ? new Color32(215, 79, 91, 255) : new Color32(64, 179, 127, 255))
                    : (x < width / 2 ? new Color32(65, 124, 211, 255) : new Color32(226, 181, 69, 255));
                if ((x / 40 + y / 40) % 2 != 0)
                    color *= 0.88f;
                if (x < 4 || y < 4 || x >= width - 4 || y >= height - 4)
                    color = new Color32(228, 236, 243, 255);
                color.a = 1;
                pixels[y * width + x] = color;
            }
        }

        pattern.SetPixels32(pixels);
        pattern.Apply(false, true);
    }

    private void OnDisable()
    {
        if (display != null)
            display.ShowWaiting();
        ReleasePattern();
    }

    private void ReleasePattern()
    {
        if (pattern == null)
            return;
        if (Application.isPlaying)
            Destroy(pattern);
        else
            DestroyImmediate(pattern);
        pattern = null;
    }
}
