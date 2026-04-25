using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NorcusSheetsManager.Application.Abstractions.Messaging;
using NorcusSheetsManager.Application.Abstractions.Models;
using NorcusSheetsManager.Application.Corrector;
using NorcusSheetsManager.Application.Corrector.CheckFileExists;
using NorcusSheetsManager.SharedKernel;
using NorcusSheetsManager.Web.Api.Authentication;
using NorcusSheetsManager.Web.Api.Extensions;
using NorcusSheetsManager.Web.Api.Infrastructure;

namespace NorcusSheetsManager.Web.Api.Endpoints.Corrector;

internal sealed class CheckFileExists : IEndpoint
{
  public void MapEndpoint(IEndpointRouteBuilder app)
  {
    app.MapGet("corrector/file-exists/{transaction}/{fileName}", async (
        string transaction,
        string fileName,
        ITokenAuthenticator auth,
        IQueryHandler<CheckFileExistsQuery, IRenamingSuggestion> handler,
        HttpContext ctx,
        CancellationToken cancellationToken) =>
    {
      if (!auth.GetAuthContext(ctx).IsAuthenticated)
      {
        return Results.Unauthorized();
      }

      if (!Guid.TryParse(transaction, out Guid transactionGuid))
      {
        return CustomResults.Problem(Result.Failure(CorrectorErrors.InvalidGuid(transaction)));
      }

      var query = new CheckFileExistsQuery
      {
        TransactionGuid = transactionGuid,
        FileName = fileName,
      };

      Result<IRenamingSuggestion> result = await handler.Handle(query, cancellationToken);
      return result.Match(Results.Ok, CustomResults.Problem);
    })
    .WithTags(Tags.Corrector)
    .WithSummary("Check whether a target filename is already used")
    .WithDescription("For an open rename transaction, checks whether the proposed filename is already in use in that folder. Only JWT validity is required — no admin or folder-membership check.")
    .Produces<IRenamingSuggestion>(StatusCodes.Status200OK)
    .WithResponseExample(StatusCodes.Status200OK, new
    {
      FileName = "hot_n_cold-kate_perry",
      FileExists = true,
    })
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status401Unauthorized)
    .ProducesProblem(StatusCodes.Status404NotFound);
  }
}
