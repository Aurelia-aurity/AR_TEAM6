using System;

public enum ScreenInstrument { Janggu, Buk, Jing, Kkwaenggwari }
public enum ScreenMode { User, Administrator }

/// <summary>Main-thread UI state. Only a matching backend acknowledgement changes running state.</summary>
public sealed class ScreenControlState
{
    public bool IsRunning { get; private set; }
    public bool IsPending { get; private set; }
    public ScreenInstrument Instrument { get; private set; }
    public ScreenMode Mode { get; private set; }
    public string Message { get; private set; } = "";
    public event Action<int, bool> RunRequested;
    public event Action<ScreenInstrument> InstrumentChanged;
    public event Action<ScreenMode> ModeChanged;
    public event Action Changed;
    private int requestId;
    private bool requestedRunning;

    public void ToggleRun()
    {
        if (IsPending) return;
        if (RunRequested == null)
        {
            Message = "실행 제어기 연결이 필요합니다.";
            Changed?.Invoke();
            return;
        }
        requestedRunning = !IsRunning;
        IsPending = true;
        Message = "";
        int id = ++requestId;
        Changed?.Invoke();
        RunRequested.Invoke(id, requestedRunning);
    }

    public void CompleteRunRequest(int id, bool succeeded, string error = null)
    {
        if (!IsPending || id != requestId) return;
        IsPending = false;
        if (succeeded) IsRunning = requestedRunning;
        Message = succeeded ? "" : (error ?? "요청을 완료하지 못했습니다. 다시 시도해 주세요.");
        Changed?.Invoke();
    }

    public void CancelPending(string reason)
    {
        if (IsPending) CompleteRunRequest(requestId, false, reason);
    }

    // Use for independently reported shutdowns/failures as well as initial backend state.
    public void ReportRunning(bool running)
    {
        IsPending = false;
        IsRunning = running;
        Message = "";
        Changed?.Invoke();
    }

    public void SelectInstrument(ScreenInstrument instrument)
    {
        if (!Enum.IsDefined(typeof(ScreenInstrument), instrument)) throw new ArgumentOutOfRangeException(nameof(instrument));
        if (Instrument == instrument) return;
        Instrument = instrument;
        InstrumentChanged?.Invoke(instrument);
        Changed?.Invoke();
    }

    public void ToggleMode()
    {
        Mode = Mode == ScreenMode.User ? ScreenMode.Administrator : ScreenMode.User;
        ModeChanged?.Invoke(Mode);
        Changed?.Invoke();
    }
}
