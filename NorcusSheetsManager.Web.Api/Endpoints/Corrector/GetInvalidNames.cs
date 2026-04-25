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
  private static readonly object[] _Example =
  [
    new
    {
      TransactionGuid = "8a7f5a4e-3d2c-4b1a-9f0e-6a7b8c9d0e1f",
      Folder = "teri",
      InvalidFileName = "Hot___cold-Kate_Perry.jpg",
      Suggestions = new[]
      {
        new { FileName = "hot_n_cold-kate_perry", FileExists = true },
        new { FileName = "hot_cold-kate_perry", FileExists = false },
      },
    },
  ];

  public void MapEndpoint(IEndpointRouteBuilder app)
  {
    app.MapGet("corrector/invalid-names/{suggestionsCount:int?}", (
        int? suggestionsCount,
        ITokenAuthenticator auth,
        IQueryHandler<GetInvalidNamesQuery, InvalidNamesResponse> handler,
        HttpContext ctx,
        CancellationToken cancellationToken)
            => HandleAsync(null, suggestionsCount, auth, handler, ctx, cancellationToken))
      .WithTags(Tags.Corrector)
      .WithSummary("List invalid filenames (N suggestions each)")
      .WithDescription("Returns invalid filenames across every folder, each with up to {suggestionsCount} rename suggestions (capped at 10). Omit {suggestionsCount} to receive one suggestion per entry. Non-admin callers receive only entries from their own folder.")
      .Produces<object[]>(StatusCodes.Status200OK)
      .WithResponseExample(StatusCodes.Status200OK, _Example)
      .ProducesProblem(StatusCodes.Status401Unauthorized);

    app.MapGet("corrector/{folder}/invalid-names/{suggestionsCount:int?}", (
        string folder,
        int? suggestionsCount,
        ITokenAuthenticator auth,
        IQueryHandler<GetInvalidNamesQuery, InvalidNamesResponse> handler,
        HttpContext ctx,
        CancellationToken cancellationToken)
            => HandleAsync(folder, suggestionsCount, auth, handler, ctx, cancellationToken))
      .WithTags(Tags.Corrector)
      .WithSummary("List invalid filenames in a folder (N suggestions each)")
      .WithDescription("Returns invalid filenames within the given folder, each with up to {suggestionsCount} rename suggestions (capped at 10). Omit {suggestionsCount} to receive one suggestion per entry. Non-admin callers requesting a folder other than their own receive HTTP 403.")
      .Produces<object[]>(StatusCodes.Status200OK)
      .WithResponseExample(StatusCodes.Status200OK, _Example)
      .ProducesProblem(StatusCodes.Status401Unauthorized);
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
