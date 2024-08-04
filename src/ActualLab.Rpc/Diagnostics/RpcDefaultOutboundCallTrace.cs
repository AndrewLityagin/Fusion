using System.Diagnostics;
using ActualLab.Rpc.Infrastructure;

namespace ActualLab.Rpc.Diagnostics;

public sealed class RpcDefaultOutboundCallTrace(Activity? activity)
    : RpcOutboundCallTrace(activity)
{
    public override void Complete(RpcOutboundCall call)
    {
        if (Activity == null)
            return;

        Activity.Finalize(call.UntypedResultTask);

        // Activity wasn't Current, so...
        var lastActivity = Activity.Current;
        Activity.Dispose();
        if (lastActivity != Activity)
            Activity.Current = lastActivity;
    }
}