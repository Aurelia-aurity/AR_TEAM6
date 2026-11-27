using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Binds the scene-authored Buk comparison controls to the shared sample player.</summary>
public sealed class BukAudioPreviewControls : MonoBehaviour
{
    [SerializeField] private InstrumentSamplePlayer player;
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
            saveTuningToggle.onValueChanged.AddListener(keep => player.KeepSessionTuning = keep);
        }

        for (int index = 0; index < InstrumentSoundProfile.StrengthCount; index++)
        {
            int sample = index;
            RefreshGain(sample);
            RefreshPitch(sample);
            gainFields[sample].onEndEdit.AddListener(value => UpdateGain(sample, value));
            pitchFields[sample].onEndEdit.AddListener(value => UpdatePitch(sample, value));
            gainSliders[sample].onValueChanged.AddListener(gain =>
            {
                player.SetGain(sample, Mathf.Round(gain * 100f) / 100f);
                RefreshGain(sample);
            });
            pitchSliders[sample].onValueChanged.AddListener(pitch =>
            {
                player.SetPitch(sample, Mathf.Round(pitch * 100f) / 100f);
                RefreshPitch(sample);
            });
        }
    }

    public void RefreshFromInspector(InstrumentSamplePlayer changedPlayer)
    {
        if (player != changedPlayer)
        {
            return;
        }

        for (int index = 0; index < InstrumentSoundProfile.StrengthCount; index++)
        {
            RefreshGain(index);
            RefreshPitch(index);
        }
    }

    private void UpdateGain(int sample, string value)
    {
        if (TryParse(value, out float gain))
        {
            player.SetGain(sample, gain);
        }

        RefreshGain(sample);
    }

    private void UpdatePitch(int sample, string value)
    {
        if (TryParse(value, out float pitch))
        {
            player.SetPitch(sample, pitch);
        }

        RefreshPitch(sample);
    }

    private void RefreshGain(int sample)
    {
        gainSliders[sample].SetValueWithoutNotify(player.GetGain(sample));
        gainFields[sample].SetTextWithoutNotify(FormatGain(sample));
    }

    private void RefreshPitch(int sample)
    {
        pitchSliders[sample].SetValueWithoutNotify(player.GetPitch(sample));
        pitchFields[sample].SetTextWithoutNotify(FormatPitch(sample));
    }

    private string FormatGain(int sample) =>
        player.GetGain(sample).ToString("0.0#", CultureInfo.InvariantCulture);

    private string FormatPitch(int sample) =>
        player.GetPitch(sample).ToString("0.00", CultureInfo.InvariantCulture);

    private static bool TryParse(string text, out float value) =>
        float.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out value) && float.IsFinite(value);
}
