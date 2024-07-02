using ActualLab.Interception;
using ActualLab.Rpc;
using ActualLab.Rpc.Clients;
using ActualLab.Rpc.Infrastructure;

namespace Samples.MeshRpc;

public sealed class RpcHelpers(Host ownHost)
{
    public RpcPeerRef RouteCall(RpcMethodDef method, ArgumentList arguments)
    {
        if (arguments.Length == 0)
            return RpcPeerRef.LocalCall;

        var arg0Type = arguments.GetType(0);
        if (arg0Type == typeof(HostRef))
            return RpcHostPeerRef.Get(arguments.Get<HostRef>(0));
        if (typeof(IHasHostRef).IsAssignableFrom(arg0Type))
            return RpcHostPeerRef.Get(arguments.Get<IHasHostRef>(0).HostRef);

        if (arg0Type == typeof(ShardRef))
            return RpcShardPeerRef.Get(arguments.Get<ShardRef>(0));
        if (typeof(IHasShardRef).IsAssignableFrom(arg0Type))
            return RpcShardPeerRef.Get(arguments.Get<IHasShardRef>(0).ShardRef);

        return RpcShardPeerRef.Get(ShardRef.New(arguments.GetUntyped(0)));

    }

    public string GetHostUrl(RpcWebSocketClient client, RpcClientPeer peer)
    {
        var hostId = peer.Ref.Key.Value;
        var host = MeshState.State.Value.HostById.GetValueOrDefault(hostId);
        return host?.Url ?? throw new RpcRerouteException($"Host '{hostId}' is already gone.");
    }
}
