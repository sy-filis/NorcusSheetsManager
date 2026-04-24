using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NorcusSheetsManager.Application.Abstractions.Messaging;
using NorcusSheetsManager.Application.Abstractions.Models;
using NorcusSheetsManager.Application.Corrector.GetInvalidNames;
using NorcusSheetsManager.SharedKernel;
using NorcusSheetsManager.Web.Api.Authentication;
using NorcusSheetsManager.Web.Api.Extensions;
using NorcusSheetsManager.Web.Api.Infrastructure;

namespace NorcusSheetsManager.Web.Api.Endpoints.Corrector;

internal sealed class GetInvalidNames : IEndpoint
{
  public void MapEndpoint(IEndpointRouteBuilder app)
  {
    app.MapGet("corrector/invalid-names/{suggestionsCount:int?}", (
        int? suggestionsCount,
        ITokenAuthenticator auth,
        IQueryHandler<GetInvalidNamesQuery, InvalidNamesResponse> handler,
        HttpContext ctx,
        CancellationToken cancellationToken)
            => HandleAsync(null, suggestionsCount, auth, handler, ctx, cancellationToken))
      .WithTags(Tags.Corrector);

    app.MapGet("corrector/{folder}/invalid-names/{suggestionsCount:int?}", (
        string folder,
        int? suggestionsCount,
        ITokenAuthenticator auth,
        IQueryHandler<GetInvalidNamesQuery, InvalidNamesResponse> handler,
        HttpContext ctx,
        CancellationToken cancellationToken)
            => HandleAsync(folder, suggestionsCount, auth, handler, ctx, cancellationToken))
      .WithTags(Tags.Corrector);
  }

  private static async Task<IResult> HandleAsync(
      string? folder,
      int? suggestionsCount,
      ITokenAuthenticator auth,
      IQueryHandler<GetInvalidNamesQuery, InvalidNamesResponse> handler,
      HttpContext ctx,
      CancellationToken cancellationToken)
  {
    AuthContext authCtx = auth.GetAuthContext(ctx);
    if (!authCtx.IsAuthenticated)
    {
      return Results.Unauthorized();
    }

    var query = new GetInvalidNamesQuery
    {
      Folder = folder,
      SuggestionsCount = suggestionsCount,
      IsAdmin = authCtx.IsAdmin,
      UserId = authCtx.UserId,
    };

    Result<InvalidNamesResponse> result = await handler.Handle(query, cancellationToken);
    return result.Match(
        value =>
        {
          Type serializationType = value.IsFolderScoped
              ? typeof(IEnumerable<IRenamingTransactionBase>)
              : typeof(IEnumerable<IRenamingTransaction>);
          string json = JsonSerializer.Serialize(value.Transactions, serializationType);
          return Results.Content(json, "application/json; charset=utf-8");
        },
        CustomResults.Problem);
  }
}
