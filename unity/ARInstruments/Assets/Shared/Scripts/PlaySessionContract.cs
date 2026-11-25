public enum InstrumentType : byte
{
    Janggu = 0,
    Buk = 1,
    Jing = 2,
    Kkwaenggwari = 3
}

public readonly struct RunSessionRequest
{
    public readonly int RequestId;
    public readonly bool Run;

    public RunSessionRequest(int requestId, bool run)
    {
        RequestId = requestId;
        Run = run;
    }
}

public readonly struct RunSessionResult
{
    public readonly int RequestId;
    public readonly bool Succeeded;
    public readonly string ErrorMessage;

    public RunSessionResult(int requestId, bool succeeded, string errorMessage = null)
    {
        RequestId = requestId;
        Succeeded = succeeded;
        ErrorMessage = errorMessage;
    }
}
