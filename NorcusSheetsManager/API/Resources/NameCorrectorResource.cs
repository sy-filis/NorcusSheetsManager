using Grapevine;
using Microsoft.Extensions.Logging;
using NLog.Filters;
using NorcusSheetsManager.NameCorrector;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace NorcusSheetsManager.API.Resources
{
    [RestResource(BasePath = "api/v1/corrector")]
    internal class NameCorrectorResource
    {
        private ITokenAuthenticator _Authenticator { get; set; }
        private Corrector _Corrector { get; set; }
        private Models.NameCorrectorModel _Model { get; set; }
        public NameCorrectorResource(ITokenAuthenticator authenticator, Corrector corrector)
        {
            _Authenticator = authenticator;
            _Corrector = corrector;
            _Model = new Models.NameCorrectorModel(corrector.DbLoader);
        }

        [RestRoute("Get", "/invalid-names")]
        [RestRoute("Get", "/invalid-names/{suggestionsCount:num}")]
        [RestRoute("Get", "/{folder}/invalid-names")]
        [RestRoute("Get", "/{folder}/invalid-names/{suggestionsCount:num}")]
        public async Task GetInvalidNames(IHttpContext context)
        {
            if (!_Authenticator.ValidateFromContext(context))
            {
                await context.Response.SendResponseAsync(HttpStatusCode.Unauthorized);
                return;
            }

            if (!_Corrector.ReloadData())
            {
                context.Response.StatusCode = HttpStatusCode.InternalServerError;
                await context.Response.SendResponseAsync($"No songs were loaded from the database.");
                return;
            }

            context.Request.PathParameters.TryGetValue("folder", out string? folder);

            // Kontrola práv uživatele
            bool isAdmin = _Authenticator.ValidateFromContext(context, new Claim("NsmAdmin", "true"));
            Guid userId = new Guid(_Authenticator.GetClaimValue(context, "uuid") ?? Guid.Empty.ToString());
            if (!_Model.CanUserRead(isAdmin, userId, ref folder))
            {
                await context.Response.SendResponseAsync(HttpStatusCode.Forbidden);
                return;
            }

            int suggestionsCount = 1;
            if (context.Request.PathParameters.TryGetValue("suggestionsCount", out string? suggestionsCountString)
                && Int32.TryParse(suggestionsCountString, out int suggestionsCountInt))
            {
                suggestionsCount = suggestionsCountInt;
            }

            IEnumerable<IRenamingTransaction>? transactions;
            Type serializationType;
            if (String.IsNullOrEmpty(folder))
            {
                transactions = _Corrector.GetRenamingTransactionsForAllSubfolders(suggestionsCount);
                serializationType = typeof(IEnumerable<IRenamingTransaction>);
            }
            else
            {
                transactions = _Corrector.GetRenamingTransactions(folder, suggestionsCount);
                serializationType = typeof(IEnumerable<IRenamingTransactionBase>);
            }

            if (transactions is null)
            {
                context.Response.StatusCode = HttpStatusCode.BadRequest;
                await context.Response.SendResponseAsync($"Bad request: Folder \"{folder ?? _Corrector.BaseSheetsFolder}\" does not exist.");
                return;
            }
            context.Response.ContentType = ContentType.Json;
            await context.Response.SendResponseAsync(JsonSerializer.Serialize(transactions, serializationType));
        }

        [RestRoute("Get", "/count")]
        [RestRoute("Get", "/{folder}/count")]
        public async Task GetCount(IHttpContext context)
        {
            if (!_Authenticator.ValidateFromContext(context))
            {
                await context.Response.SendResponseAsync(HttpStatusCode.Unauthorized);
                return;
            }

            if (!_Corrector.ReloadData())
            {
                await context.Response.SendResponseAsync(HttpStatusCode.InternalServerError, $"No songs were loaded from the database.");
                return;
            }

            context.Request.PathParameters.TryGetValue("folder", out string? folder);

            // Kontrola práv uživatele
            bool isAdmin = _Authenticator.ValidateFromContext(context, new Claim("NsmAdmin", "true"));
            Guid userId = new Guid(_Authenticator.GetClaimValue(context, "uuid") ?? Guid.Empty.ToString());
            if (!_Model.CanUserRead(isAdmin, userId, ref folder))
            {
                await context.Response.SendResponseAsync(HttpStatusCode.Forbidden);
                return;
            }

            IEnumerable<IRenamingTransaction>? transactions;
            if (String.IsNullOrEmpty(folder))
                transactions = _Corrector.GetRenamingTransactionsForAllSubfolders(1);
            else
                transactions = _Corrector.GetRenamingTransactions(folder, 1);

            if (transactions is null)
            {
                await context.Response.SendResponseAsync(HttpStatusCode.BadRequest, $"Bad request: Folder \"{folder ?? _Corrector.BaseSheetsFolder}\" does not exist.");
                return;
            }

            await context.Response.SendResponseAsync(transactions.Count().ToString());
        }

        [RestRoute("Post", "fix-name")]
        public async Task FixName(IHttpContext context)
        {
            if (!_Authenticator.ValidateFromContext(context))
            {
                await context.Response.SendResponseAsync(HttpStatusCode.Unauthorized);
                return;
            }
            
            // Kontrola práv uživatele
            bool isAdmin = _Authenticator.ValidateFromContext(context, new Claim("NsmAdmin", "true"));
            Guid userId = new Guid(_Authenticator.GetClaimValue(context, "uuid") ?? Guid.Empty.ToString());
            if (!_Model.CanUserCommit(isAdmin, userId))
            {
                await context.Response.SendResponseAsync(HttpStatusCode.Forbidden);
                return;
            }

            var request = JsonSerializer.Deserialize(context.Request.InputStream, typeof(RequestClasses.PostFixName)) as RequestClasses.PostFixName;
            if (request is null)
            {
                await context.Response.SendResponseAsync(HttpStatusCode.BadRequest);
                return;
            }

            if (String.IsNullOrEmpty(request.FileName) && !request.SuggestionIndex.HasValue)
            {
                string msg = "Bad request: " 
                    + "Both \"FileName\" and \"SuggestionIndex\" values are null. One of them must be set.";
                if (request.TransactionGuid == Guid.Empty)
                    msg += " Parameter \"TransactionGuid\" is invalid";

                await context.Response.SendResponseAsync(HttpStatusCode.BadRequest, msg);
                return;
            }

            var response = request.SuggestionIndex.HasValue ? 
                _Corrector.CommitTransactionByGuid(request.TransactionGuid, (int)request.SuggestionIndex)
                : _Corrector.CommitTransactionByGuid(request.TransactionGuid, request.FileName!);

            if (!response.Success)
            {
                await context.Response.SendResponseAsync(HttpStatusCode.InternalServerError, response.Message);
                return;
            }

            context.Response.ContentType = ContentType.Json;
            await context.Response.SendResponseAsync();
        }

        [RestRoute("Delete", "{transaction}")]
        public async Task DeleteFile(IHttpContext context)
        {
            if (!_Authenticator.ValidateFromContext(context))
            {
                await context.Response.SendResponseAsync(HttpStatusCode.Unauthorized);
                return;
            }

            // Kontrola práv uživatele
            bool isAdmin = _Authenticator.ValidateFromContext(context, new Claim("NsmAdmin", "true"));
            Guid userId = new Guid(_Authenticator.GetClaimValue(context, "uuid") ?? Guid.Empty.ToString());
            if (!_Model.CanUserCommit(isAdmin, userId))
            {
                await context.Response.SendResponseAsync(HttpStatusCode.Forbidden);
                return;
            }

            context.Request.PathParameters.TryGetValue("transaction", out string? guidString);
            if (!Guid.TryParse(guidString, out Guid guid))
            {
                await context.Response.SendResponseAsync(HttpStatusCode.BadRequest, $"Bad request: Parameter \"{guidString}\" is not valid Guid.");
                return;
            }

            var response = _Corrector.DeleteTransaction(guid);

            if (!response.Success)
            {
                await context.Response.SendResponseAsync(HttpStatusCode.InternalServerError, response.Message);
                return;
            }

            context.Response.StatusCode = HttpStatusCode.Ok;
            await context.Response.SendResponseAsync();
        }

        [RestRoute("Get", "/file-exists/{transaction}/{fileName}")]
        public async Task CheckFileExists(IHttpContext context)
        {
            if (!_Authenticator.ValidateFromContext(context))
            {
                await context.Response.SendResponseAsync(HttpStatusCode.Unauthorized);
                return;
            }

            StringBuilder errorMsg = new StringBuilder();

            context.Request.PathParameters.TryGetValue("transaction", out string? guidString);
            if (!Guid.TryParse(guidString, out Guid guid))
                errorMsg.AppendLine($"Parameter \"{guidString}\" is not valid Guid.");

            var trans = _Corrector.GetTransactionByGuid(guid);
            if (trans is null)
                errorMsg.AppendLine($"Transaction \"{guid}\" does not exist.");

            if(errorMsg.Length > 0)
            {
                await context.Response.SendResponseAsync(HttpStatusCode.BadRequest, $"Bad request: " + errorMsg.ToString());
                return;
            }

            context.Request.PathParameters.TryGetValue("fileName", out string? fileName);
            IRenamingSuggestion suggestion = new Suggestion(trans!.InvalidFullPath, fileName ?? "", 0);
            context.Response.ContentType = ContentType.Json;
            await context.Response.SendResponseAsync(JsonSerializer.Serialize(suggestion));
        }
    }
}
