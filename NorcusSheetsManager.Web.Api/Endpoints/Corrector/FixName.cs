using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NorcusSheetsManager.Application.Abstractions.Messaging;
using NorcusSheetsManager.Application.Corrector.FixName;
using NorcusSheetsManager.SharedKernel;
using NorcusSheetsManager.Web.Api.Authentication;
using NorcusSheetsManager.Web.Api.Extensions;
using NorcusSheetsManager.Web.Api.Infrastructure;

namespace NorcusSheetsManager.Web.Api.Endpoints.Corrector;

internal sealed class FixName : IEndpoint
{
  public sealed class Request
  {
    public Guid TransactionGuid { get; set; }
    public string? FileName { get; set; }
    public int? SuggestionIndex { get; set; }
  }

  public void MapEndpoint(IEndpointRouteBuilder app)
  {
    app.MapPost("corrector/fix-name", async (
        Request request,
        ITokenAuthenticator auth,
        ICommandHandler<FixNameCommand> handler,
        HttpContext ctx,
        CancellationToken cancellationToken) =>
    {
      AuthContext authCtx = auth.GetAuthContext(ctx);
      if (!authCtx.IsAuthenticated)
      {
        return Results.Unauthorized();
      }

      var command = new FixNameCommand
      {
        TransactionGuid = request.TransactionGuid,
        FileName = request.FileName,
        SuggestionIndex = request.SuggestionIndex,
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
