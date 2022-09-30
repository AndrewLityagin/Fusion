using Stl.OS;

namespace Stl.Fusion.Internal;

public sealed class ComputedGraphPruner : WorkerBase
{
    public record Options
    {
        public bool AutoActivate { get; init; } = true;
        public RandomTimeSpan CheckPeriod { get; init; } = TimeSpan.FromMinutes(10).ToRandom(0.1);
        public RandomTimeSpan NextBatchDelay { get; init; } = TimeSpan.FromSeconds(0.1).ToRandom(0.25);
        public int BatchSize { get; init; } = 1024 * HardwareInfo.GetProcessorCountPo2Factor();
    }

    public Options Settings { get; init; }
    public IMomentClock Clock { get; init; }
    public ILogger Log { get; init; }

    public Task<Unit> WhenActivated { get; }

    public ComputedGraphPruner(Options settings, ILogger<ComputedGraphPruner>? log = null)
        : this(settings, MomentClockSet.Default, log) { }
    public ComputedGraphPruner(Options settings, MomentClockSet clocks, ILogger<ComputedGraphPruner>? log = null)
    {
        Settings = settings;
        Clock = clocks.CpuClock;
        Log = log ?? NullLogger<ComputedGraphPruner>.Instance;
        WhenActivated = TaskSource.New<Unit>(true).Task;

        if (settings.AutoActivate)
            Start();
    }

    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        var computedRegistry = ComputedRegistry.Instance;
        if (Settings.AutoActivate) {
            // This prevents race condition when two pruners are assigned at almost
            // the same time - they'll both may end up activate themselves here
            var oldGraphPruner = computedRegistry.GraphPruner;
            while (oldGraphPruner != this) {
                await oldGraphPruner.WhenActivated.ConfigureAwait(false);
                oldGraphPruner = computedRegistry.ChangeGraphPruner(this, oldGraphPruner);
            }
        }
        else if (computedRegistry.GraphPruner != this) {
            Log.LogWarning("Terminating: ComputedRegistry.Instance.GraphPruner != this");
            return;
        }
        TaskSource.For(WhenActivated).TrySetResult(default);

        var activitySource = GetType().GetActivitySource();

        var runChain = new AsyncChain("Prune()", async ct => {
            var computedRegistry = ComputedRegistry.Instance;
            using var keyEnumerator = computedRegistry.Keys.GetEnumerator();
            var computedCount = 0L;
            var consistentCount = 0L;
            var edgeCount = 0L;
            var removedEdgeCount = 0L;
            var remainingBatchCapacity = Settings.BatchSize;
            var batchCount = 0;
            while (keyEnumerator.MoveNext()) {
                if (0 >= --remainingBatchCapacity) {
                    await Clock.Delay(Settings.NextBatchDelay.Next(), ct).ConfigureAwait(false);
                    remainingBatchCapacity = Settings.BatchSize;
                    batchCount++;
                }

                var computedInput = keyEnumerator.Current!;
                computedCount++;
                if (computedRegistry.Get(computedInput) is IComputedImpl computed && computed.IsConsistent()) {
                    consistentCount++;
                    var (oldEdgeCount, newEdgeCount) = computed.PruneUsedBy();
                    edgeCount += oldEdgeCount;
                    removedEdgeCount += oldEdgeCount - newEdgeCount;
                }
            }
            Log.LogInformation(
                "Processed {ConsistentCount}/{ComputedCount} computed instances " +
                "and removed {RemovedEdgeCount}/{EdgeCount} \"used by\" edges " +
                "in {BatchCount} batches (x {BatchSize})",
                consistentCount, computedCount, removedEdgeCount, edgeCount, batchCount + 1, Settings.BatchSize);
        }).Trace(() => activitySource.StartActivity("Prune"), Log).Silence();

        var chain = runChain.AppendDelay(Settings.CheckPeriod, Clock)
            .CycleForever()
            .LogBoundary(Log);

        cancellationToken.ThrowIfCancellationRequested();
        await chain.Start(cancellationToken).ConfigureAwait(false);
    }
}