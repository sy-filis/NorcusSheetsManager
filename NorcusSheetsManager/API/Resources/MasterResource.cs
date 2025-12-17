using Grapevine;
using NorcusSheetsManager.NameCorrector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace NorcusSheetsManager.API.Resources
{
    [RestResource]
    internal class MasterResource
    {
        private readonly ITokenAuthenticator _authenticator;
        private readonly Corrector _corrector;

        public MasterResource(ITokenAuthenticator authenticator, Corrector corrector)
        {
            this._authenticator = authenticator;
            this._corrector = corrector;
        }

        [RestRoute("Options")]
        public async Task Options(IHttpContext context)
        {
            context.Response.AddHeader("Access-Control-Max-Age", "86400");
            await context.Response.SendResponseAsync(HttpStatusCode.Ok);
        }

        [RestRoute("Get", "api/v1/folders")]
        public async Task GetFolders(IHttpContext context)
        {
            if (!_authenticator.ValidateFromContext(context))
            {
                await context.Response.SendResponseAsync(HttpStatusCode.Forbidden);
                return;
            }

            var folders = Directory.GetDirectories(_corrector.BaseSheetsFolder)
                .Select(d => d.Split("\\").Last())
                .Where(d => !d.StartsWith("."));

            context.Response.ContentType = ContentType.Json;
            await context.Response.SendResponseAsync(JsonSerializer.Serialize(folders));
        }
    }
}
