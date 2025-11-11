using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Grapevine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using NorcusSheetsManager.NameCorrector;

namespace NorcusSheetsManager.API
{
    public class Server
    {
        private static Server? __instance;
        private static Server _Instance 
        {
            get 
            {
                if (__instance is null) throw new Exception("Server is not initialized. Call " + nameof(Initialize));
                return __instance; 
            }
        }
        private IRestServer _server;
        private Server(int port, string secureKey, List<(Type type, object instance)> singletons)
        {
            RestServerBuilder serverBuilder = RestServerBuilder.From<Startup>();
            serverBuilder.Services.AddSingleton<ITokenAuthenticator>(new JWTAuthenticator(secureKey));

            foreach (var s in singletons)
            {
                serverBuilder.Services.AddSingleton(s.type, s.instance);
            }

            _server = serverBuilder.Build();
            _server.Prefixes.Clear();
            _server.Prefixes.Add($"http://+:{port}/");
            //_server.Prefixes.Add($"http://localhost:{port}/");
            _server.Router.BeforeRoutingAsync.Add(_BeforeRouting);
        }
        internal static void Initialize(int port, string secureKey, List<(Type type, object instance)> singletons) 
        {
            if (__instance != null) throw new Exception("Instace is already created.");
            __instance = new Server(port, secureKey, singletons);
        }
        public static void Start() => _Instance._server.Start();
        public static void Stop() => _Instance._server.Stop();
        private Task _BeforeRouting(IHttpContext context)
        {
            context.Response.AddHeader("Access-Control-Allow-Origin", "*");
            context.Response.AddHeader("Access-Control-Allow-Methods", "*");
            context.Response.AddHeader("Access-Control-Allow-Headers", "*");
            return Task.CompletedTask;
        }
    }
}
