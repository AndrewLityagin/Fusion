namespace ActualLab.Rpc.Infrastructure;

public static class WellKnownRpcHeaders
{
    public static readonly RpcHeaderKey Hash = new((Symbol)"#");
    public static readonly RpcHeaderKey Version = new((Symbol)"v"); // Unused from ProtocolVersion >= 2
    public static readonly RpcHeaderKey ActivityId = new((Symbol)"~");
    public static readonly RpcHeaderKey W3CTraceParent = new((Symbol)"~p");
    public static readonly RpcHeaderKey W3CTraceState = new((Symbol)"~s");

    public static IReadOnlyDictionary<Symbol, RpcHeaderKey> ByName { get; private set; }
    public static IReadOnlyDictionary<ByteString, RpcHeaderKey> ByUtf8Name { get; private set; }

    public static void Set(params RpcHeaderKey[] headers)
    {
        ByName = headers.ToDictionary(x => x.Name);
        ByUtf8Name = headers.ToDictionary(x => new ByteString(x.Utf8Name));
    }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor.
    static WellKnownRpcHeaders()
        => Set(Hash, Version, ActivityId, W3CTraceParent, W3CTraceState);
#pragma warning restore CS8618

}
