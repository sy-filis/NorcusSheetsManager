using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NorcusSheetsManager.Application.Abstractions.Messaging;
using NorcusSheetsManager.Application.Corrector;
using NorcusSheetsManager.Application.Corrector.DeleteInvalidFile;
using NorcusSheetsManager.SharedKernel;
using NorcusSheetsManager.Web.Api.Authentication;
using NorcusSheetsManager.Web.Api.Extensions;
using NorcusSheetsManager.Web.Api.Infrastructure;

namespace NorcusSheetsManager.Web.Api.Endpoints.Corrector;

internal sealed class DeleteInvalidFile : IEndpoint
{
  public void MapEndpoint(IEndpointRouteBuilder app)
  {
    app.MapDelete("corrector/{transaction}", async (
        string transaction,
        ITokenAuthenticator auth,
        ICommandHandler<DeleteInvalidFileCommand> handler,
        HttpContext ctx,
        CancellationToken cancellationToken) =>
    {
      AuthContext authCtx = auth.GetAuthContext(ctx);
      if (!authCtx.IsAuthenticated)
      {
        return Results.Unauthorized();
      }

      if (!Guid.TryParse(transaction, out Guid transactionGuid))
      {
        return CustomResults.Problem(Result.Failure(CorrectorErrors.InvalidGuid(transaction)));
      }

      var command = new DeleteInvalidFileCommand
      {
        TransactionGuid = transactionGuid,
        IsAdmin = authCtx.IsAdmin,
        UserId = authCtx.UserId,
      };

      Result result = await handler.Handle(command, cancellationToken);
      return result.Match(() => Results.Ok(), CustomResults.Problem);
    })
    .WithTags(Tags.Corrector)
    .Produces(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status401Unauthorized)
    .ProducesProblem(StatusCodes.Status404NotFound);
  }
}
