using Microsoft.AspNetCore.Http;

namespace ActualLab.Rpc.Server;

public delegate RpcPeerRef RpcWebSocketServerPeerRefFactory(RpcWebSocketServer server, HttpContext context);

public static class RpcWebSocketServerDefaultDelegates
{
    public static RpcWebSocketServerPeerRefFactory PeerRefFactory { get; set; } =
        static (server, context) => {
            var query = context.Request.Query;
            var clientId = query[server.Settings.ClientIdParameterName].SingleOrDefault() ?? "";
            return RpcPeerRef.NewServer(clientId);
        };
}
