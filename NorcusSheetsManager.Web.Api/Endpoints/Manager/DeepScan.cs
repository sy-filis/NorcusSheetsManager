using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NorcusSheetsManager.Application.Abstractions.Messaging;
using NorcusSheetsManager.Application.Manager.Scan;
using NorcusSheetsManager.SharedKernel;
using NorcusSheetsManager.Web.Api.Authentication;
using NorcusSheetsManager.Web.Api.Extensions;
using NorcusSheetsManager.Web.Api.Infrastructure;

namespace NorcusSheetsManager.Web.Api.Endpoints.Manager;

internal sealed class DeepScan : IEndpoint
{
  public void MapEndpoint(IEndpointRouteBuilder app)
  {
    app.MapPost("manager/deep-scan", async (
        ITokenAuthenticator auth,
        ICommandHandler<DeepScanCommand> handler,
        HttpContext ctx,
        CancellationToken cancellationToken) =>
    {
      IResult? authFailure = auth.RequireAdmin(ctx);
      if (authFailure is not null)
      {
        return authFailure;
      }

      Result result = await handler.Handle(new DeepScanCommand(), cancellationToken);
      return result.Match(() => Results.Ok(), CustomResults.Problem);
    })
    .WithTags(Tags.Manager)
    .WithSummary("Run a Deep Scan")
    .WithDescription("Verifies every PDF has the correct number of images for its page count, and reconverts mismatches. Returns 200 immediately; the scan runs in the background. Admin only.")
    .Produces(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status401Unauthorized)
    .ProducesProblem(StatusCodes.Status403Forbidden);
  }
}
