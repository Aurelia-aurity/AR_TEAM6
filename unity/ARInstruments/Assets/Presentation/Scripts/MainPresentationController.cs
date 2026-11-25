using UnityEngine;

/// <summary>
/// Connects ScreenControlBar UI state and active instrument models in Main.unity
/// to the shared GameEventBus without cross-module assembly references.
/// </summary>
[DisallowMultipleComponent]
public sealed class MainPresentationController : MonoBehaviour
{
    [SerializeField] private ScreenControlBar controlBar;
    [SerializeField] private GameObject jangguObject;
    [SerializeField] private GameObject bukObject;
    [SerializeField] private CalibrationManager calibrationManager;

    private bool isSyncingInstrument;

    private void OnEnable()
    {
        if (controlBar == null)
        {
            controlBar = GetComponent<ScreenControlBar>();
        }

        if (controlBar != null)
        {
            controlBar.State.RunRequested += OnRunRequested;
            controlBar.State.InstrumentChanged += OnUiInstrumentChanged;
            controlBar.State.ModeChanged += OnUiModeChanged;
        }

        GameEventBus.OnRunSessionCompleted += OnRunSessionCompleted;
        GameEventBus.OnSessionRunningChanged += OnSessionRunningChanged;
        GameEventBus.OnInstrumentSelected += OnBusInstrumentSelected;

        ScreenInstrument initialInstrument = controlBar != null
            ? controlBar.State.Instrument
            : (ScreenInstrument)GameEventBus.SelectedInstrument;
        ApplyInstrumentVisual(initialInstrument);
        GameEventBus.PublishInstrumentSelected((InstrumentType)initialInstrument);

        if (controlBar != null && !controlBar.State.IsPending
            && controlBar.State.IsRunning != GameEventBus.IsSessionRunning)
        {
            controlBar.State.ReportRunning(GameEventBus.IsSessionRunning);
        }
    }

    private void OnDisable()
    {
        if (controlBar != null)
        {
            controlBar.State.RunRequested -= OnRunRequested;
            controlBar.State.InstrumentChanged -= OnUiInstrumentChanged;
            controlBar.State.ModeChanged -= OnUiModeChanged;
        }

        GameEventBus.OnRunSessionCompleted -= OnRunSessionCompleted;
        GameEventBus.OnSessionRunningChanged -= OnSessionRunningChanged;
        GameEventBus.OnInstrumentSelected -= OnBusInstrumentSelected;
    }

    private void OnRunRequested(int requestId, bool requestedRunning)
    {
        GameEventBus.PublishRunSessionRequest(new RunSessionRequest(requestId, requestedRunning));
    }

    private void OnRunSessionCompleted(RunSessionResult result)
    {
        controlBar?.State.CompleteRunRequest(result.RequestId, result.Succeeded, result.ErrorMessage);
    }

    private void OnSessionRunningChanged(bool isRunning)
    {
        if (controlBar != null && !controlBar.State.IsPending && controlBar.State.IsRunning != isRunning)
        {
            controlBar.State.ReportRunning(isRunning);
        }
    }

    private void OnUiInstrumentChanged(ScreenInstrument instrument)
    {
        if (isSyncingInstrument)
        {
            return;
        }

        isSyncingInstrument = true;
        try
        {
            ApplyInstrumentVisual(instrument);
            GameEventBus.PublishInstrumentSelected((InstrumentType)instrument);
        }
        finally
        {
            isSyncingInstrument = false;
        }
    }

    private void OnBusInstrumentSelected(InstrumentType instrument)
    {
        if (isSyncingInstrument)
        {
            return;
        }

        isSyncingInstrument = true;
        try
        {
            ScreenInstrument uiInstrument = (ScreenInstrument)instrument;
            if (controlBar != null && controlBar.State.Instrument != uiInstrument)
            {
                controlBar.State.SelectInstrument(uiInstrument);
            }

            ApplyInstrumentVisual(uiInstrument);
        }
        finally
        {
            isSyncingInstrument = false;
        }
    }

    private void OnUiModeChanged(ScreenMode mode)
    {
        if (mode == ScreenMode.Administrator && calibrationManager != null
            && calibrationManager.CurrentStep == CalibrationStep.NotStarted)
        {
            // Keep calibration ready when switching to Administrator mode without interrupting active play.
            calibrationManager.enabled = true;
        }
    }

    private void ApplyInstrumentVisual(ScreenInstrument instrument)
    {
        if (jangguObject != null)
        {
            jangguObject.SetActive(instrument == ScreenInstrument.Janggu);
        }

        if (bukObject != null)
        {
            bukObject.SetActive(instrument == ScreenInstrument.Buk);
        }
    }
}
