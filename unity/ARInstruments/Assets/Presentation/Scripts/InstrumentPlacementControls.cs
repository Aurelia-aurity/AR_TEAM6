using UnityEngine;
using UnityEngine.UI;

/// <summary>Preview-only pose controls. Transforms stay in world space, outside the UI mirror.</summary>
[DisallowMultipleComponent]
public sealed class InstrumentPlacementControls : MonoBehaviour
{
    [SerializeField] private Transform instrument;
    [SerializeField] private Slider[] positionSliders = new Slider[3];
    [SerializeField] private Slider[] rotationSliders = new Slider[3];
    [SerializeField] private Slider scaleSlider;
    [SerializeField] private Text[] valueLabels = new Text[7];
    [SerializeField] private Button resetButton;

    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private Vector3 initialScale;
    public Transform Instrument => instrument;

    private void Awake()
    {
        if (instrument == null || scaleSlider == null || resetButton == null
            || positionSliders.Length != 3 || rotationSliders.Length != 3 || valueLabels.Length != 7)
        {
            Debug.LogError("InstrumentPlacementControls requires the instrument and seven pose controls.", this);
            enabled = false;
            return;
        }
        for (int i = 0; i < 3; i++)
        {
            if (positionSliders[i] == null || rotationSliders[i] == null)
            {
                Debug.LogError("Instrument pose sliders are missing.", this);
                enabled = false;
                return;
            }
        }
        initialPosition = instrument.position;
        initialRotation = instrument.rotation;
        initialScale = instrument.localScale;
        ResetPose();
    }

    private void OnEnable()
    {
        foreach (Slider slider in positionSliders)
            if (slider != null) { slider.onValueChanged.AddListener(ApplySliders); slider.interactable = true; }
        foreach (Slider slider in rotationSliders)
            if (slider != null) { slider.onValueChanged.AddListener(ApplySliders); slider.interactable = true; }
        if (scaleSlider != null) { scaleSlider.onValueChanged.AddListener(ApplySliders); scaleSlider.interactable = true; }
        if (resetButton != null) { resetButton.onClick.AddListener(ResetPose); resetButton.interactable = true; }
    }

    private void OnDisable()
    {
        foreach (Slider slider in positionSliders)
            if (slider != null) { slider.onValueChanged.RemoveListener(ApplySliders); slider.interactable = false; }
        foreach (Slider slider in rotationSliders)
            if (slider != null) { slider.onValueChanged.RemoveListener(ApplySliders); slider.interactable = false; }
        if (scaleSlider != null) { scaleSlider.onValueChanged.RemoveListener(ApplySliders); scaleSlider.interactable = false; }
        if (resetButton != null) { resetButton.onClick.RemoveListener(ResetPose); resetButton.interactable = false; }
    }

    public void ResetPose()
    {
        if (instrument == null) return;
        instrument.SetPositionAndRotation(initialPosition, initialRotation);
        instrument.localScale = initialScale;
        Vector3 angles = initialRotation.eulerAngles;
        for (int i = 0; i < 3; i++)
        {
            positionSliders[i].SetValueWithoutNotify(initialPosition[i]);
            rotationSliders[i].SetValueWithoutNotify(Mathf.DeltaAngle(0, angles[i]));
        }
        scaleSlider.SetValueWithoutNotify(1);
        UpdateLabels();
    }

    private void ApplySliders(float unused)
    {
        Vector3 position = new Vector3(positionSliders[0].value, positionSliders[1].value, positionSliders[2].value);
        Vector3 angles = new Vector3(rotationSliders[0].value, rotationSliders[1].value, rotationSliders[2].value);
        instrument.SetPositionAndRotation(position, Quaternion.Euler(angles));
        instrument.localScale = initialScale * scaleSlider.value;
        UpdateLabels();
    }

    private void UpdateLabels()
    {
        for (int i = 0; i < 3; i++)
        {
            if (valueLabels[i] != null) valueLabels[i].text = positionSliders[i].value.ToString("F2") + " m";
            if (valueLabels[i + 3] != null) valueLabels[i + 3].text = rotationSliders[i].value.ToString("F0") + " deg";
        }
        if (valueLabels[6] != null) valueLabels[6].text = scaleSlider.value.ToString("F2") + "x";
    }
}
