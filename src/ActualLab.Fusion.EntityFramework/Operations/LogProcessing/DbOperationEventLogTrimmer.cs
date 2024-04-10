using ActualLab.Fusion.EntityFramework.LogProcessing;
using Microsoft.EntityFrameworkCore;

namespace ActualLab.Fusion.EntityFramework.Operations.LogProcessing;

public class DbOperationEventLogTrimmer<TDbContext>
    : DbLogTrimmer<TDbContext, DbOperation, DbOperationEventLogTrimmer<TDbContext>.Options>
    where TDbContext : DbContext
{
    public record Options : DbLogTrimmerOptions
    {
        public static Options Default { get; set; } = new();

        public Options()
            => MaxEntryAge = TimeSpan.FromDays(1);
    }

    protected override IState<ImmutableHashSet<DbShard>> WorkerShards => DbHub.ShardRegistry.EventProcessorShards;

    // ReSharper disable once ConvertToPrimaryConstructor
    public DbOperationEventLogTrimmer(Options settings, IServiceProvider services)
        : base(settings, services)
    { }
}