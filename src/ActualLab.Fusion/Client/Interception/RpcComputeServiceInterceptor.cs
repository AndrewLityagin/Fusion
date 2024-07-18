using System.Diagnostics.CodeAnalysis;
using ActualLab.Fusion.Interception;
using ActualLab.Fusion.Internal;
using ActualLab.Interception;
using ActualLab.Rpc;
using ActualLab.Rpc.Infrastructure;

namespace ActualLab.Fusion.Client.Interception;

public class RpcComputeServiceInterceptor : ComputeServiceInterceptor
{
    public new record Options : ComputeServiceInterceptor.Options
    {
        public static new Options Default { get; set; } = new();
    }

    public readonly RpcInterceptor RegularCallRpcInterceptor;
    public readonly RpcInterceptor ComputeCallRpcInterceptor;
    public readonly RpcHub RpcHub;
    public readonly object? LocalTarget;

    // ReSharper disable once ConvertToPrimaryConstructor
    public RpcComputeServiceInterceptor(
        Options settings,
        RpcInterceptor regularCallRpcInterceptor,
        RpcInterceptor computeCallRpcInterceptor,
        object? localTarget,
        FusionInternalHub hub
        ) : base(settings, hub)
    {
        RegularCallRpcInterceptor = regularCallRpcInterceptor;
        ComputeCallRpcInterceptor = computeCallRpcInterceptor;
        RpcHub = regularCallRpcInterceptor.Hub;
        LocalTarget = localTarget;
    }

    public override Func<Invocation, object?>? SelectHandler(in Invocation invocation)
        => GetHandler(invocation) // Compute service method
            ?? RegularCallRpcInterceptor.SelectHandler(invocation); // Regular or command service method

    protected override Func<Invocation, object?>? CreateHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TUnwrapped>
        (Invocation initialInvocation, MethodDef methodDef)
    {
        var computeMethodDef = (ComputeMethodDef)methodDef;
        var rpcMethodDef = (RpcMethodDef?)RegularCallRpcInterceptor.GetMethodDef(initialInvocation.Method, initialInvocation.Proxy.GetType());
        var function = rpcMethodDef == null
            ? new ComputeMethodFunction<TUnwrapped>(computeMethodDef, Hub) // No RpcMethodDef -> it's a local call
            : new RpcComputeMethodFunction<TUnwrapped>(computeMethodDef, rpcMethodDef, LocalTarget, Hub);
        return CreateHandler(function);
    }

    protected override MethodDef? CreateMethodDef(MethodInfo method, Type proxyType)
        // This interceptor is created on per-service basis, so to reuse the validation cache,
        // we redirect this call to Hub.ComputeServiceInterceptor, which is a singleton.
        => Hub.ComputeServiceInterceptor.GetMethodDef(method, proxyType);

    protected override void ValidateTypeInternal(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
        // This interceptor is created on per-service basis, so to reuse the validation cache,
        // we redirect this call to Hub.ComputeServiceInterceptor, which is a singleton.
        => Hub.ComputeServiceInterceptor.ValidateType(type);
}
