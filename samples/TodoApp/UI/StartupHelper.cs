using Blazorise;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using ActualLab.Fusion.Blazor;
using ActualLab.Fusion.Blazor.Authentication;
using ActualLab.Fusion.Client.Caching;
using ActualLab.Fusion.Diagnostics;
using ActualLab.Fusion.Extensions;
using ActualLab.Fusion.Client.Interception;
using ActualLab.Fusion.UI;
using ActualLab.OS;
using ActualLab.Rpc;
using Blazorise.Bootstrap5;
using Blazorise.Icons.FontAwesome;
using Templates.TodoApp.Abstractions;
using Templates.TodoApp.UI.Services;

namespace Templates.TodoApp.UI;

#pragma warning disable IL2026

public static class StartupHelper
{
    public static void ConfigureServices(IServiceCollection services, WebAssemblyHostBuilder builder)
    {
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Logging.AddFilter(typeof(App).Namespace, LogLevel.Information);
        builder.Logging.AddFilter(typeof(Computed).Namespace, LogLevel.Information);
        builder.Logging.AddFilter(typeof(InMemoryRemoteComputedCache).Namespace, LogLevel.Information);
        builder.Logging.AddFilter(typeof(RpcHub).Namespace, LogLevel.Debug);
        builder.Logging.AddFilter(typeof(CommandHandlerResolver).Namespace, LogLevel.Debug);

        // Fusion services
        var fusion = services.AddFusion();

        fusion.AddInMemoryRemoteComputedCache(_ => new() { LogLevel = LogLevel.Information });
        fusion.AddAuthClient();
        fusion.AddBlazor().AddAuthentication().AddPresenceReporter();

        // RPC clients
        fusion.AddClient<ITodoService>();
        fusion.Rpc.AddClient<IRpcExampleService>();

        ConfigureSharedServices(services, HostKind.Client, builder.HostEnvironment.BaseAddress);
    }

    public static void ConfigureSharedServices(IServiceCollection services, HostKind hostKind, string remoteRpcHostUrl)
    {
        var fusion = services.AddFusion();
        fusion.AddComputedGraphPruner(_ => new() {
            CheckPeriod = TimeSpan.FromSeconds(10) // You should stick to defaults here, it's just for testing
        });
        fusion.AddFusionTime(); // Add it only if you use it

        if (hostKind != HostKind.BackendServer) {
            // Client and API host use RPC client
            fusion.Rpc.AddWebSocketClient(remoteRpcHostUrl);
            if (hostKind == HostKind.ApiServer)
                // ApiServer should always go to BackendServer's /backend/rpc/ws endpoint
                RpcPeerRef.Default = RpcPeerRef.GetDefaultPeerRef(isBackend: true);

            // Client and SSB services
            ComputedState.DefaultOptions.FlowExecutionContext = true; // To preserve current culture
            fusion.AddService<TodoUI>(ServiceLifetime.Scoped);
            services.AddScoped(c => new RpcPeerStateMonitor(c, OSInfo.IsAnyClient ? RpcPeerRef.Default : null));
            services.AddScoped<IUpdateDelayer>(c => new UpdateDelayer(c.UIActionTracker(), 0.25)); // 0.25s

            // Blazorise
            services.AddBlazorise(options => options.Immediate = true)
                .AddBootstrap5Providers()
                .AddFontAwesomeIcons();
        }
        else {
        }

        // Diagnostics
        if (hostKind == HostKind.Client)
            RpcPeer.DefaultCallLogLevel = LogLevel.Debug;
        services.AddHostedService(c => {
            var isWasm = OSInfo.IsWebAssembly;
            return new FusionMonitor(c) {
                SleepPeriod = isWasm
                    ? TimeSpan.Zero
                    : TimeSpan.FromMinutes(1).ToRandom(0.25),
                CollectPeriod = TimeSpan.FromSeconds(isWasm ? 3 : 60),
                AccessFilter = isWasm
                    ? static computed => computed.Input.Function is IRemoteComputeMethodFunction
                    : static _ => true,
                AccessStatisticsPreprocessor = StatisticsPreprocessor,
                RegistrationStatisticsPreprocessor = StatisticsPreprocessor,
            };

            void StatisticsPreprocessor(Dictionary<string, (int, int)> stats)
            {
                foreach (var key in stats.Keys.ToList()) {
                    if (key.Contains(".Pseudo"))
                        stats.Remove(key);
                    if (key.StartsWith("FusionTime."))
                        stats.Remove(key);
                }
            }
        });
    }
}
