using System.Diagnostics.CodeAnalysis;
using ActualLab.Collections.Slim;
using ActualLab.Conversion;
using ActualLab.Fusion.Internal;
using ActualLab.Fusion.Operations.Internal;
using ActualLab.Resilience;
using ActualLab.Versioning;
using Errors = ActualLab.Fusion.Internal.Errors;

namespace ActualLab.Fusion;

public interface IComputed : IResult, IHasVersion<ulong>
{
    ComputedOptions Options { get; }
    ComputedInput Input { get; }
    ConsistencyState ConsistencyState { get; }
    Type OutputType { get; }
    IResult Output { get; }
    Task OutputAsTask { get; }
    event Action<IComputed> Invalidated;

    void Invalidate(bool immediately = false);
    TResult Apply<TArg, TResult>(IComputedApplyHandler<TArg, TResult> handler, TArg arg);

    ValueTask<IComputed> Update(CancellationToken cancellationToken = default);
    ValueTask<object> Use(CancellationToken cancellationToken = default);
}

public abstract class Computed<T> : IComputedImpl, IResult<T>
{
    private readonly ComputedOptions _options;
    private volatile int _state;
    private volatile ComputedFlags _flags;
    private long _lastKeepAliveSlot;
    private Result<T> _output;
    private Task<T>? _outputAsTask;
    private RefHashSetSlim3<IComputedImpl> _used;
    private HashSetSlim3<(ComputedInput Input, ulong Version)> _usedBy;
    // ReSharper disable once InconsistentNaming
    private InvalidatedHandlerSet _invalidated;

    protected object Lock {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this;
    }

    protected ComputedFlags Flags {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _flags;
    }

    // IComputed properties

    public ComputedOptions Options {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _options;
    }

    public ComputedInput Input { get; }

    public ConsistencyState ConsistencyState {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (ConsistencyState)_state;
    }

    public IFunction<T> Function => (IFunction<T>)Input.Function;
    public ulong Version { get; } = ComputedVersion.Next();
    public Type OutputType => typeof(T);

    public virtual Result<T> Output {
        get {
            this.AssertConsistencyStateIsNot(ConsistencyState.Computing);
            return _output;
        }
    }

    public Task<T> OutputAsTask {
        get {
            if (_outputAsTask != null)
                return _outputAsTask;

            lock (Lock) {
                this.AssertConsistencyStateIsNot(ConsistencyState.Computing);
                return _outputAsTask ??= _output.AsTask();
            }
        }
    }

    // IResult<T> properties
    public T? ValueOrDefault => Output.ValueOrDefault;
    public T Value => Output.Value;
    public Exception? Error => Output.Error;
    public bool HasValue => Output.HasValue;
    public bool HasError => Output.HasError;

    // "Untyped" versions of properties
    ComputedInput IComputed.Input => Input;
    // ReSharper disable once HeapView.BoxingAllocation
    IResult IComputed.Output => Output;
    // ReSharper disable once HeapView.BoxingAllocation
    object? IResult.UntypedValue => Output.Value;
    Task IComputed.OutputAsTask => OutputAsTask;

    public event Action<IComputed> Invalidated {
        add {
            if (ConsistencyState == ConsistencyState.Invalidated) {
                value(this);
                return;
            }
            lock (Lock) {
                if (ConsistencyState == ConsistencyState.Invalidated) {
                    value(this);
                    return;
                }
                _invalidated.Add(value);
            }
        }
        remove {
            lock (Lock) {
                if (ConsistencyState == ConsistencyState.Invalidated)
                    return;
                _invalidated.Remove(value);
            }
        }
    }

    protected Computed(ComputedOptions options, ComputedInput input)
    {
        _options = options;
        Input = input;
    }

    protected Computed(ComputedOptions options, ComputedInput input, Result<T> output, bool isConsistent)
    {
        _options = options;
        Input = input;
        _state = (int)(isConsistent ? ConsistencyState.Consistent : ConsistencyState.Invalidated);
        _output = output;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Deconstruct(out T value, out Exception? error)
        => Output.Deconstruct(out value, out error);

    public void Deconstruct(out T value, out Exception? error, out ulong version)
    {
        Output.Deconstruct(out value, out error);
        version = Version;
    }

    public override string ToString()
        => $"{GetType().GetName()}({Input} v.{Version.FormatVersion()}, State: {ConsistencyState})";

    // GetHashCode

    public override int GetHashCode() => (int)Version;

    // TrySetOutput

    public bool TrySetOutput(Result<T> output)
    {
        ComputedFlags flags;
        lock (Lock) {
            if (ConsistencyState != ConsistencyState.Computing)
                return false;

            SetStateUnsafe(ConsistencyState.Consistent);
            _output = output;
            flags = _flags;
        }

        if ((flags & ComputedFlags.InvalidateOnSetOutput) != 0) {
            Invalidate((flags & ComputedFlags.InvalidateOnSetOutputImmediately) != 0);
            return true;
        }

        StartAutoInvalidation();
        return true;
    }

    // Invalidate

    public void Invalidate(bool immediately = false)
    {
        if (ConsistencyState == ConsistencyState.Invalidated)
            return;

        // Debug.WriteLine($"{nameof(Invalidate)}: {this}");
        lock (Lock) {
            var flags = _flags;
            switch (ConsistencyState) {
            case ConsistencyState.Invalidated:
                return;
            case ConsistencyState.Computing:
                flags |= ComputedFlags.InvalidateOnSetOutput;
                if (immediately)
                    flags |= ComputedFlags.InvalidateOnSetOutputImmediately;
                _flags = flags;
                return;
            default: // == ConsistencyState.Computed
                immediately |= Options.InvalidationDelay <= TimeSpan.Zero;
                if (immediately) {
                    SetStateUnsafe(ConsistencyState.Invalidated);
                    break;
                }

                if ((flags & ComputedFlags.DelayedInvalidationStarted) != 0)
                    return; // Already started

                _flags = flags | ComputedFlags.DelayedInvalidationStarted;
                break;
            }
        }

        if (!immediately) {
            // Delayed invalidation
            this.Invalidate(Options.InvalidationDelay);
            return;
        }

        // Instant invalidation - it may happen just once,
        // so we don't need a lock here.
        try {
            try {
                StaticLog.For<IComputed>().LogWarning("Invalidating: {Computed}", this);
                OnInvalidated();
                _invalidated.Invoke(this);
                _invalidated = default;
            }
            finally {
                // Any code called here may not throw
                _used.Apply(this, (self, c) => c.RemoveUsedBy(self));
                _used.Clear();
                _usedBy.Apply(default(Unit), static (_, usedByEntry) => {
                    var c = usedByEntry.Input.GetExistingComputed();
                    if (c != null && c.Version == usedByEntry.Version)
                        c.Invalidate(); // Invalidate doesn't throw - ever
                });
                _usedBy.Clear();
            }
        }
        catch (Exception e) {
            // We should never throw errors during the invalidation
            try {
                var log = Input.Function.Services.LogFor(GetType());
                log.LogError(e, "Error while invalidating {Category}", Input.Category);
            }
            catch {
                // Intended: Invalidate doesn't throw!
            }
        }
    }

    protected virtual void OnInvalidated()
        => CancelTimeouts();

    protected void StartAutoInvalidation()
    {
        if (!this.IsConsistent())
            return;

        TimeSpan timeout;
        var error = _output.Error;
        if (error == null) {
            timeout = _options.AutoInvalidationDelay;
            if (timeout != TimeSpan.MaxValue)
                this.Invalidate(timeout);
            return;
        }

        if (error is OperationCanceledException) {
            // This error requires instant invalidation
            Invalidate(true);
            return;
        }

        timeout = IsTransientError(error)
            ? _options.TransientErrorInvalidationDelay
            : _options.AutoInvalidationDelay;
        if (timeout != TimeSpan.MaxValue)
            this.Invalidate(timeout);
    }

    // Update

    async ValueTask<IComputed> IComputed.Update(CancellationToken cancellationToken)
        => await Update(cancellationToken).ConfigureAwait(false);
    public async ValueTask<Computed<T>> Update(CancellationToken cancellationToken = default)
    {
        if (this.IsConsistent())
            return this;

        using var scope = Computed.BeginIsolation();
        return await Function.Invoke(Input, scope.Context, cancellationToken).ConfigureAwait(false);
    }

    // Use

    async ValueTask<object> IComputed.Use(CancellationToken cancellationToken)
        => (await Use(cancellationToken).ConfigureAwait(false))!;
    public virtual async ValueTask<T> Use(CancellationToken cancellationToken = default)
    {
        var context = ComputeContext.Current;
        if ((context.CallOptions & CallOptions.GetExisting) != 0) // Both GetExisting & Invalidate
            throw Errors.InvalidContextCallOptions(context.CallOptions);

        // Slightly faster version of this.TryUseExistingFromLock(context)
        if (this.IsConsistent()) {
            // It can become inconsistent here, but we don't care, since...
            this.UseNew(context);
            // it can also become inconsistent here & later, and UseNew handles this.
            // So overall, Use(...) guarantees the dependency chain will be there even
            // if computed is invalidated right after above "if".
            return Value;
        }

        var computed = await Function.Invoke(Input, context, cancellationToken).ConfigureAwait(false);
        return computed.Value;
    }

    // Apply

    public TResult Apply<TArg, TResult>(IComputedApplyHandler<TArg, TResult> handler, TArg arg)
        => handler.Apply(this, arg);

    // IResult<T> methods

    public bool IsValue([MaybeNullWhen(false)] out T value)
        => Output.IsValue(out value);
    public bool IsValue([MaybeNullWhen(false)] out T value, [MaybeNullWhen(true)] out Exception error)
        => Output.IsValue(out value, out error!);
    public Result<T> AsResult()
        => Output.AsResult();
    public Result<TOther> Cast<TOther>()
        => Output.Cast<TOther>();
    T IConvertibleTo<T>.Convert() => Value;
    Result<T> IConvertibleTo<Result<T>>.Convert() => AsResult();

    // IComputedImpl methods

    void IGenericTimeoutHandler.OnTimeout()
        => Invalidate(true);

    IComputedImpl[] IComputedImpl.Used => Used;
    protected internal IComputedImpl[] Used {
        get {
            var result = new IComputedImpl[_used.Count];
            lock (Lock) {
                _used.CopyTo(result);
                return result;
            }
        }
    }

    (ComputedInput Input, ulong Version)[] IComputedImpl.UsedBy => UsedBy;
    protected internal (ComputedInput Input, ulong Version)[] UsedBy {
        get {
            var result = new (ComputedInput Input, ulong Version)[_usedBy.Count];
            lock (Lock) {
                _usedBy.CopyTo(result);
                return result;
            }
        }
    }

    void IComputedImpl.AddUsed(IComputedImpl used) => AddUsed(used);
    protected internal void AddUsed(IComputedImpl used)
    {
        // Debug.WriteLine($"{nameof(AddUsed)}: {this} <- {used}");
        lock (Lock) {
            if (ConsistencyState != ConsistencyState.Computing) {
                // The current computed is either:
                // - Invalidated: nothing to do in this case.
                //   Deps are meaningless for whatever is already invalidated.
                // - Consistent: this means the dependency computation hasn't been completed
                //   while the dependant was computing, which literally means it is actually unused.
                //   This happens e.g. when N tasks to compute dependencies start during the computation,
                //   but only some of them are awaited. Other results might be ignored e.g. because
                //   an exception was thrown in one of early "awaits". And if you "linearize" such a
                //   method, it becomes clear that dependencies that didn't finish by the end of computation
                //   actually aren't used, coz in the "linear" flow they would be requested at some
                //   later point.
                return;
            }
            if (used.AddUsedBy(this))
                _used.Add(used);
        }
    }

    bool IComputedImpl.AddUsedBy(IComputedImpl usedBy) => AddUsedBy(usedBy);
    protected internal bool AddUsedBy(IComputedImpl usedBy)
    {
        lock (Lock) {
            switch (ConsistencyState) {
            case ConsistencyState.Computing:
                throw Errors.WrongComputedState(ConsistencyState);
            case ConsistencyState.Invalidated:
                usedBy.Invalidate();
                return false;
            }

            var usedByRef = (usedBy.Input, usedBy.Version);
            _usedBy.Add(usedByRef);
            return true;
        }
    }

    void IComputedImpl.RemoveUsedBy(IComputedImpl usedBy) => RemoveUsedBy(usedBy);
    protected internal void RemoveUsedBy(IComputedImpl usedBy)
    {
        lock (Lock) {
            if (ConsistencyState == ConsistencyState.Invalidated)
                // _usedBy is already empty or going to be empty soon;
                // moreover, only Invalidated code can modify
                // _used/_usedBy once invalidation flag is set
                return;

            _usedBy.Remove((usedBy.Input, usedBy.Version));
        }
    }

    (int OldCount, int NewCount) IComputedImpl.PruneUsedBy() => PruneUsedBy();
    protected internal (int OldCount, int NewCount) PruneUsedBy()
    {
        lock (Lock) {
            if (ConsistencyState != ConsistencyState.Consistent)
                // _usedBy is already empty or going to be empty soon;
                // moreover, only Invalidated code can modify
                // _used/_usedBy once invalidation flag is set
                return (0, 0);

            var replacement = new HashSetSlim3<(ComputedInput Input, ulong Version)>();
            var oldCount = _usedBy.Count;
            foreach (var entry in _usedBy.Items) {
                var c = entry.Input.GetExistingComputed();
                if (c != null && c.Version == entry.Version)
                    replacement.Add(entry);
            }
            _usedBy = replacement;
            return (oldCount, _usedBy.Count);
        }
    }

    void IComputedImpl.CopyUsedTo(ref ArrayBuffer<IComputedImpl> buffer) => CopyUsedTo(ref buffer);
    protected internal void CopyUsedTo(ref ArrayBuffer<IComputedImpl> buffer)
    {
        lock (Lock) {
            var count = buffer.Count;
            buffer.EnsureCapacity(count + _used.Count);
            _used.CopyTo(buffer.Buffer.AsSpan(count));
        }
    }

    void IComputedImpl.RenewTimeouts(bool isNew) => RenewTimeouts(isNew);
    protected internal void RenewTimeouts(bool isNew)
    {
        if (ConsistencyState == ConsistencyState.Invalidated)
            return; // We shouldn't register miss here, since it's going to be counted later anyway

        var minCacheDuration = Options.MinCacheDuration;
        if (minCacheDuration != default) {
            var keepAliveSlot = Timeouts.GetKeepAliveSlot(Timeouts.Clock.Now + minCacheDuration);
            var lastKeepAliveSlot = Interlocked.Exchange(ref _lastKeepAliveSlot, keepAliveSlot);
            if (lastKeepAliveSlot != keepAliveSlot)
                Timeouts.KeepAlive.AddOrUpdateToLater(this, keepAliveSlot);
        }

        ComputedRegistry.Instance.ReportAccess(this, isNew);
    }

    void IComputedImpl.CancelTimeouts() => CancelTimeouts();
    protected internal void CancelTimeouts()
    {
        var options = Options;
        if (options.MinCacheDuration != default) {
            Interlocked.Exchange(ref _lastKeepAliveSlot, 0);
            Timeouts.KeepAlive.Remove(this);
        }
    }

    // Protected & private methods

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetStateUnsafe(ConsistencyState newState)
        => _state = (int)newState;

    bool IComputedImpl.IsTransientError(Exception error) => IsTransientError(error);
    protected internal bool IsTransientError(Exception error)
    {
        if (error is OperationCanceledException)
            return true; // Must be transient under any circumstances in IComputed

        TransiencyResolver<IComputed>? transiencyResolver = null;
        try {
            var services = Input.Function.Services;
            transiencyResolver = services.GetService<TransiencyResolver<IComputed>>();
        }
        catch (ObjectDisposedException) {
            // We want to handle IServiceProvider disposal gracefully
        }
        return transiencyResolver?.Invoke(error).IsTransient()
            ?? TransiencyResolvers.PreferTransient.Invoke(error).IsTransient();
    }
}
