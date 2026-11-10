using UnityEngine;
using UnityEngine.UI;

public enum CameraDisplayStatus : byte
{
    Waiting,
    Streaming,
    Error
}

/// <summary>
/// Displays externally owned video textures on the Unity main thread.
/// Does not open cameras, estimate tracking coordinates, or own the texture lifetime.
/// </summary>
[DisallowMultipleComponent]
public sealed class CameraDisplayView : MonoBehaviour
{
    [SerializeField] private RawImage cameraImage;
    [SerializeField] private AspectRatioFitter viewportAspect;
    [SerializeField] private GameObject waitingPlaceholder;
    [SerializeField] private Text placeholderTitle;
    [SerializeField] private Text placeholderHint;
    [SerializeField] private Image statusIndicator;
    [SerializeField] private Text statusText;
    [SerializeField] private Text statusDetail;
    [SerializeField] private RectTransform mirrorRoot;
    [SerializeField] private Toggle mirrorToggle;

    private int rotation;
    private bool mirrorEnabled;

    public CameraDisplayStatus Status { get; private set; }
    public bool MirrorEnabled => mirrorEnabled;
    public float VideoAspectRatio => viewportAspect != null ? viewportAspect.aspectRatio : 16f / 9f;

    private bool HasReferences => cameraImage != null && viewportAspect != null
        && waitingPlaceholder != null && placeholderTitle != null && placeholderHint != null
        && statusIndicator != null && statusText != null && statusDetail != null
        && mirrorRoot != null && mirrorToggle != null;

    private void Awake()
    {
        if (!HasReferences)
        {
            Debug.LogError("CameraDisplayView requires its display and status UI references.", this);
            enabled = false;
            return;
        }

        ShowWaiting();
    }

    private void OnEnable()
    {
        Canvas.willRenderCanvases += RefreshImageSize;
        if (HasReferences)
        {
            mirrorToggle.onValueChanged.AddListener(SetMirrorMode);
            mirrorToggle.interactable = true;
            SetMirrorMode(mirrorEnabled);
        }
    }

    private void OnDisable()
    {
        Canvas.willRenderCanvases -= RefreshImageSize;
        if (mirrorToggle != null)
        {
            mirrorToggle.onValueChanged.RemoveListener(SetMirrorMode);
            mirrorToggle.interactable = false;
        }
        ShowWaiting();
    }

    /// <summary>
    /// Reflects only the displayed video and display-space overlays horizontally.
    /// Input rotation/vertical correction happens first. World/tracking data is untouched.
    /// The selection is retained while waiting, on input errors, and across view re-enables.
    /// </summary>
    public void SetMirrorMode(bool value)
    {
        mirrorEnabled = value;
        if (mirrorRoot != null)
            mirrorRoot.localScale = new Vector3(value ? -1 : 1, 1, 1);
        if (mirrorToggle != null)
            mirrorToggle.SetIsOnWithoutNotify(value);
    }

    private void LateUpdate()
    {
        if (Status == CameraDisplayStatus.Streaming && cameraImage.texture == null)
            ShowWaiting();

        RefreshImageSize();
    }

    /// <summary>
    /// Call when a usable frame arrives or its dimensions/orientation change.
    /// Rotation is clockwise in multiples of 90 degrees. The source's vertical
    /// correction is applied in texture space before rotation, independently of mirror mode.
    /// The provider must call ShowWaiting/ShowError when its input stops or fails.
    /// </summary>
    public bool PresentFrame(Texture texture, int clockwiseRotation = 0,
        bool verticallyMirrored = false, string description = null)
    {
        if (!isActiveAndEnabled || !HasReferences)
            return false;

        if (texture == null || texture.width <= 0 || texture.height <= 0)
        {
            ShowWaiting();
            return false;
        }

        if (clockwiseRotation % 90 != 0)
        {
            ShowError("The video orientation is not supported.");
            return false;
        }

        rotation = (clockwiseRotation % 360 + 360) % 360;
        bool swapsAxes = rotation == 90 || rotation == 270;
        viewportAspect.aspectRatio = swapsAxes
            ? texture.height / (float)texture.width
            : texture.width / (float)texture.height;

        RectTransform imageRect = cameraImage.rectTransform;
        imageRect.anchorMin = imageRect.anchorMax = imageRect.pivot = new Vector2(0.5f, 0.5f);
        imageRect.anchoredPosition = Vector2.zero;
        imageRect.localScale = Vector3.one;
        imageRect.localRotation = Quaternion.Euler(0, 0, -rotation);
        cameraImage.uvRect = verticallyMirrored ? new Rect(0, 1, 1, -1) : new Rect(0, 0, 1, 1);
        cameraImage.texture = texture;
        cameraImage.color = Color.white;
        cameraImage.enabled = true;
        mirrorRoot.gameObject.SetActive(true);
        waitingPlaceholder.SetActive(false);
        Status = CameraDisplayStatus.Streaming;
        statusIndicator.color = new Color32(87, 204, 189, 255);
        statusText.text = "Video input active";
        statusDetail.text = string.IsNullOrEmpty(description) ? "Video input is available." : description;
        RefreshImageSize();
        return true;
    }

    public void ShowWaiting(string description = null)
    {
        if (!HasReferences)
            return;

        ClearFrame();
        Status = CameraDisplayStatus.Waiting;
        placeholderTitle.text = "Waiting for video input";
        placeholderHint.text = "The camera preview will appear here.";
        statusIndicator.color = new Color32(229, 176, 91, 255);
        statusText.text = "Waiting for camera";
        statusDetail.text = string.IsNullOrEmpty(description) ? "No video feed is available." : description;
    }

    public void ShowError(string description = null)
    {
        if (!HasReferences)
            return;

        ClearFrame();
        Status = CameraDisplayStatus.Error;
        placeholderTitle.text = "Camera unavailable";
        placeholderHint.text = "Check the video input and try again.";
        statusIndicator.color = new Color32(238, 116, 116, 255);
        statusText.text = "Video input error";
        statusDetail.text = string.IsNullOrEmpty(description) ? "The video input could not be displayed." : description;
    }

    private void ClearFrame()
    {
        cameraImage.enabled = false;
        cameraImage.texture = null;
        mirrorRoot.gameObject.SetActive(false);
        cameraImage.uvRect = new Rect(0, 0, 1, 1);
        cameraImage.rectTransform.localRotation = Quaternion.identity;
        rotation = 0;
        viewportAspect.aspectRatio = 16f / 9f;
        waitingPlaceholder.SetActive(true);
    }

    private void RefreshImageSize()
    {
        if (Status != CameraDisplayStatus.Streaming || cameraImage == null || viewportAspect == null)
            return;

        Vector2 size = ((RectTransform)viewportAspect.transform).rect.size;
        if (rotation == 90 || rotation == 270)
            size = new Vector2(size.y, size.x);

        if (cameraImage.rectTransform.sizeDelta != size)
            cameraImage.rectTransform.sizeDelta = size;
    }
}
