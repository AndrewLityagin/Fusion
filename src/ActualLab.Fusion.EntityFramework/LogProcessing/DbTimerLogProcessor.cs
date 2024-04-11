using ActualLab.Resilience;
using Microsoft.EntityFrameworkCore;

namespace ActualLab.Fusion.EntityFramework.LogProcessing;

public abstract class DbTimerLogProcessor<TDbContext, TDbEntry, TOptions>(
    TOptions settings,
    IServiceProvider services
    ) : DbShardWorkerBase<TDbContext>(services), IDbLogProcessor
    where TDbContext : DbContext
    where TDbEntry : class, IDbTimerLogEntry
    where TOptions : DbTimerLogProcessorOptions
{
    protected Dictionary<(DbShard Shard, Symbol Uuid), Task> ReprocessTasks { get; } = new();

    protected IMomentClock SystemClock { get; init; } = services.Clocks().SystemClock;
    protected ILogger? DefaultLog => Log.IfEnabled(Settings.LogLevel);
    protected ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug);

    public TOptions Settings { get; } = settings;
    public DbLogKind LogKind => DbLogKind.Timers;

    protected abstract Task Process(DbShard shard, TDbEntry entry, CancellationToken cancellationToken);

    protected override Task OnRun(DbShard shard, CancellationToken cancellationToken)
        => new AsyncChain($"{nameof(ProcessExpiredEntries)}[{shard}]", ct => ProcessExpiredEntries(shard, ct))
            .RetryForever(Settings.RetryDelays, SystemClock, Log)
            .CycleForever()
            .Log(Log)
            .Start(cancellationToken);

    protected virtual async Task ProcessExpiredEntries(DbShard shard, CancellationToken cancellationToken)
    {
        while (true) { // Reading entries in batches
            var mustContinue = await ProcessBatch(shard, cancellationToken).ConfigureAwait(false);
            if (!mustContinue)
                break;
        }
        await SystemClock.Delay(Settings.CheckPeriod.Next(), cancellationToken).ConfigureAwait(false);
    }

    protected virtual async Task<bool> ProcessBatch(DbShard shard, CancellationToken cancellationToken)
    {
        using var _ = ActivitySource.StartActivity().AddShardTags(shard);
        var dbContext = await DbHub.CreateDbContext(shard, readWrite: true, cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);
        var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var _2 = tx.ConfigureAwait(false);
        dbContext.EnableChangeTracking(false);

        var now = SystemClock.Now.ToDateTime();
        var batchSize = Settings.BatchSize;
        var dbEntries = dbContext.Set<TDbEntry>();
        var entries = await dbEntries.WithHints(LogKind.GetReadBatchQueryHints())
            .Where(o => o.State == LogEntryState.New && o.FiresAt < now)
            .OrderBy(o => o.FiresAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var logLevel = entries.Count == batchSize ? LogLevel.Warning : LogLevel.Debug;
        // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
        Log.IfEnabled(logLevel)?.Log(logLevel,
            $"{nameof(ProcessBatch)}[{{Shard}}]: fetched {{Count}}/{{BatchSize}} expired entries",
            shard.Value, entries.Count, batchSize);

        if (entries.Count == 0)
            return false;

        await GetProcessTasks(shard, entries, cancellationToken)
            .Collect(Settings.ConcurrencyLevel)
            .ConfigureAwait(false);

        foreach (var entry in entries) {
            dbEntries.Attach(entry);
            UpdateEntryState(dbEntries, entry, LogEntryState.Processed);
        }
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        return entries.Count >= batchSize;
    }

    protected virtual IEnumerable<Task> GetProcessTasks(
        DbShard shard, List<TDbEntry> entries, CancellationToken cancellationToken)
    {
        foreach (var entry in entries)
            yield return ProcessSafe(shard, entry, fallbackToReprocess: true, cancellationToken);
    }

    protected async Task ProcessSafe(
        DbShard shard, TDbEntry entry, bool fallbackToReprocess, CancellationToken cancellationToken)
    {
        // This method should never fail (unless cancelled)!
        if (entry.State != LogEntryState.New)
            return;

        try {
            await Process(shard, entry, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            var suffix = fallbackToReprocess ? ", will reprocess it" : "";
            // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
            Log.LogError(e,
                $"{nameof(Process)}[{{Shard}}]: failed for entry #{{Uuid}}{suffix}",
                shard, entry.Uuid);
            if (fallbackToReprocess)
                ReprocessSafe(shard, entry.Uuid, entry);
        }
    }

    protected void ReprocessSafe(DbShard shard, Symbol uuid, TDbEntry? foundEntry)
    {
        // This method should never fail!
        lock (ReprocessTasks) {
            var key = (shard, uuid);
            if (ReprocessTasks.ContainsKey(key))
                return;

            var task = Task.Run(() => Reprocess(shard, uuid, foundEntry, StopToken));
            ReprocessTasks[key] = task;
            _ = task.ContinueWith(_ => {
                lock (ReprocessTasks)
                    ReprocessTasks.Remove(key);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    protected async Task Reprocess(
        DbShard shard, Symbol uuid, TDbEntry? foundEntry, CancellationToken cancellationToken)
    {
        for (var i = 0; i < 2; i++) {
            var (mustDiscard, sProcess, sProcessed, sErrorExtra) = i switch {
                0 => (false, "process", "processed", ", will try to discard it"),
                _ => (true, "discard", "discarded", ""),
            };
            try {
                await Task.Delay(Settings.ReprocessDelay.Next(), cancellationToken).ConfigureAwait(false);
                var isProcessed = await Settings.ReprocessPolicy
                    .Apply(ct => ReprocessImpl(shard, uuid, mustDiscard, ct), cancellationToken)
                    .ConfigureAwait(false);
                if (isProcessed) {
                    // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
                    DefaultLog?.Log(Settings.LogLevel,
                        $"{nameof(Reprocess)}[{{Shard}}]: entry #{{Uuid}} is {sProcessed}",
                        shard, uuid);
                    return;
                }

                // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
                DebugLog?.LogDebug(
                    $"{nameof(Reprocess)}[{{Shard}}]: entry #{{Uuid}} is handled by another host",
                    shard, uuid);
                return;
            }
            catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
                Log.LogError(e,
                    $"{nameof(Reprocess)}[{{Shard}}]: failed to {sProcess} entry #{{Uuid}}{sErrorExtra}",
                    shard, uuid);
            }
        }
    }

    protected async Task<bool> ReprocessImpl(
        DbShard shard, Symbol uuid, bool mustDiscard, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(shard, readWrite: true, cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);
        dbContext.EnableChangeTracking(false);

        var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var _2 = tx.ConfigureAwait(false);

        var entry = await GetEntry(dbContext, uuid, cancellationToken).ConfigureAwait(false);
        if (entry == null)
            throw new LogEntryNotFoundException();
        if (entry.State != LogEntryState.New)
            return false;

        var dbEntries = dbContext.Set<TDbEntry>();
        dbEntries.Attach(entry);
        UpdateEntryState(dbEntries, entry, mustDiscard ? LogEntryState.Discarded : LogEntryState.Processed);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // If we're here, the entry's row is x-locked due to update above
        if (!mustDiscard)
            await Process(shard, entry, cancellationToken).ConfigureAwait(false);

        if (DbHub.ChaosMaker.IsEnabled)
            await DbHub.ChaosMaker.Act(this, cancellationToken).ConfigureAwait(false);
        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    protected async Task<TDbEntry?> GetEntry(TDbContext dbContext, Symbol uuid, CancellationToken cancellationToken)
        => await dbContext.Set<TDbEntry>(LogKind.GetReadOneQueryHints())
            .FirstOrDefaultAsync(x => x.Uuid == uuid.Value, cancellationToken)
            .ConfigureAwait(false);

    protected void UpdateEntryState(DbSet<TDbEntry> dbEntries, TDbEntry entry, LogEntryState state)
    {
        dbEntries.Update(entry);
        entry.State = state;
        entry.Version = DbHub.VersionGenerator.NextVersion(entry.Version);
    }
}
