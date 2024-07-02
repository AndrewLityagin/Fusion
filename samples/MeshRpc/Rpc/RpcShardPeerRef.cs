using System.Collections.Concurrent;
using ActualLab.Collections;
using ActualLab.Fusion;
using ActualLab.Rpc;
using ActualLab.Text;

namespace Samples.MeshRpc;

public sealed record RpcShardPeerRef : RpcPeerRef
{
    private static readonly ConcurrentDictionary<ShardRef, RpcShardPeerRef> Cache = new();

    public int ShardKey { get; }
    public Symbol HostId { get; }
    public override CancellationToken RerouteToken { get; }

    public static RpcShardPeerRef Get(ShardRef shardRef)
    {
        while (true) {
            var peerRef = Cache.GetOrAdd(shardRef, key => new RpcShardPeerRef(key));
            if (!peerRef.RerouteToken.IsCancellationRequested)
                return peerRef;

            Cache.TryRemove(shardRef, peerRef);
        }
    }

    public RpcShardPeerRef(ShardRef shardRef)
        : base($"{shardRef} -> {MeshState.State.Value.GetShardHost(shardRef).Id.Value}")
    {
        ShardKey = shardRef.Key;
        HostId = Key.Value.Split(" -> ")[1];
        var rerouteTokenSource = new CancellationTokenSource();
        RerouteToken = rerouteTokenSource.Token;
        _ = Task.Run(async () => {
            await MeshState.State.When(x => !x.HostById.ContainsKey(HostId)).ConfigureAwait(false);
            rerouteTokenSource.Cancel();
        });
    }

    public override RpcPeerConnectionKind GetConnectionKind(RpcHub hub)
    {
        var ownHost = hub.Services.GetRequiredService<Host>();
        return HostId == ownHost.Id ? RpcPeerConnectionKind.LocalCall : RpcPeerConnectionKind.Remote;
    }
}
