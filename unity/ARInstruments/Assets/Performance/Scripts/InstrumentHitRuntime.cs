using System;
using System.Collections.Generic;

/// <summary>Connects tracked mallet events to a set of placed instruments' pads.</summary>
public sealed class InstrumentHitRuntime : IDisposable
{
    private readonly InstrumentPad[] pads;
    public HitDetectionCore Detection { get; } = new HitDetectionCore();
    private bool disposed;

    public InstrumentHitRuntime(IReadOnlyList<InstrumentPad> pads)
    {
        if (pads == null) throw new ArgumentNullException(nameof(pads));
        this.pads = new InstrumentPad[pads.Count];
        for (int i = 0; i < pads.Count; i++) this.pads[i] = pads[i];
        ResetTracking();
    }

    public void Subscribe()
    {
        if (disposed) throw new ObjectDisposedException(nameof(InstrumentHitRuntime));
        Detection.Subscribe();
    }

    /// <summary>Clears tracking history and hit locks while retaining the configured pads.</summary>
    public void ResetTracking()
    {
        if (disposed) throw new ObjectDisposedException(nameof(InstrumentHitRuntime));
        Detection.ClearPads();
        foreach (InstrumentPad pad in pads) Detection.RegisterPad(pad);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Detection.Dispose();
    }
}
