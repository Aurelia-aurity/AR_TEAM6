using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

public sealed class JangguAudioPreviewControls : MonoBehaviour
{
    [SerializeField] private JangguAudioPreview preview;
    [SerializeField] private InputField[] gainFields;
    [SerializeField] private InputField[] pitchFields;

    [SerializeField] private Slider[] gainSliders;
    [SerializeField] private Slider[] pitchSliders;
    [SerializeField] private Toggle saveTuningToggle;

    private void Awake()
    {
        if (saveTuningToggle != null)
        {
            saveTuningToggle.gameObject.SetActive(Application.isEditor);
            saveTuningToggle.SetIsOnWithoutNotify(false);
            saveTuningToggle.onValueChanged.AddListener(keep => preview.KeepSessionTuning = keep);
        }

        for (int index = 0; index < JangguSoundProfile.SampleCount; index++)
        {
            int sample = index;
            RefreshGain(sample);
            RefreshPitch(sample);
            gainFields[sample].onEndEdit.AddListener(value => UpdateGain(sample, value));
            pitchFields[sample].onEndEdit.AddListener(value => UpdatePitch(sample, value));
            gainSliders[sample].onValueChanged.AddListener(gain =>
            {
                preview.SetGain(sample, Mathf.Round(gain * 100f) / 100f);
                RefreshGain(sample);
            });
            pitchSliders[sample].onValueChanged.AddListener(pitch =>
            {
                preview.SetPitch(sample, Mathf.Round(pitch * 100f) / 100f);
                RefreshPitch(sample);
            });
        }
    }

    public void RefreshFromInspector(JangguAudioPreview changedPreview)
    {
        if (preview != changedPreview)
        {
            return;
        }

        for (int index = 0; index < JangguSoundProfile.SampleCount; index++)
        {
            RefreshGain(index);
            RefreshPitch(index);
        }
    }

    private void UpdateGain(int sample, string value)
    {
        if (TryParse(value, out float gain))
        {
            preview.SetGain(sample, gain);
        }

        RefreshGain(sample);
    }

    private void UpdatePitch(int sample, string value)
    {
        if (TryParse(value, out float pitch))
        {
            preview.SetPitch(sample, pitch);
        }

        RefreshPitch(sample);
    }

    private void RefreshGain(int sample)
    {
        gainSliders[sample].SetValueWithoutNotify(preview.GetGain(sample));
        gainFields[sample].SetTextWithoutNotify(FormatGain(sample));
    }

    private void RefreshPitch(int sample)
    {
        pitchSliders[sample].SetValueWithoutNotify(preview.GetPitch(sample));
        pitchFields[sample].SetTextWithoutNotify(FormatPitch(sample));
    }

    private string FormatGain(int sample) =>
        preview.GetGain(sample).ToString("0.0#", CultureInfo.InvariantCulture);

    private string FormatPitch(int sample) =>
        preview.GetPitch(sample).ToString("0.00", CultureInfo.InvariantCulture);

    private static bool TryParse(string text, out float value) =>
        float.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out value) && float.IsFinite(value);
}
