namespace ActualLab.Fusion.Internal;

public readonly struct ComputeContextScope : IDisposable
{
    private readonly ComputeContext _oldContext;

    public readonly ComputeContext Context;

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal ComputeContextScope(ComputeContext context)
    {
        _oldContext = ComputeContext.Current;
        Context = context;
        if (_oldContext != context)
            ComputeContext.Current = context;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        if (_oldContext != Context)
            ComputeContext.Current = _oldContext;
    }
}
