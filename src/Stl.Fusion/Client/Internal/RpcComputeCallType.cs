using ActualLab.Rpc;

namespace ActualLab.Fusion.Client.Internal;

public static class RpcComputeCallType
{
    public static readonly byte Id = 1;

    public static void Register()
        => RpcCallTypeRegistry.Register(Id, typeof(RpcInboundComputeCall<>), typeof(RpcOutboundComputeCall<>));
}
