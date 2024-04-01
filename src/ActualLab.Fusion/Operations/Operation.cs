using ActualLab.Fusion.Operations.Internal;

namespace ActualLab.Fusion.Operations;

public class Operation(
    Symbol id,
    Symbol agentId,
    Moment startTime,
    Moment commitTime,
    ICommand command,
    OptionSet items,
    List<NestedOperation> nestedOperations
    ) : IRequirementTarget
{
    public long? Index { get; set; }
    public Symbol Id { get; set; } = id;
    public Symbol AgentId { get; set; } = agentId;
    public Moment StartTime { get; set; } = startTime; // Always UTC
    public Moment CommitTime { get; set; } = commitTime; // Always UTC
    public ICommand Command { get; set; } = command;
    public OptionSet Items { get; set; } = items;
    public List<NestedOperation> NestedOperations { get; set; } = nestedOperations;

    public Operation()
        : this(default, default, default, default, null!, new(), new())
    { }
}
