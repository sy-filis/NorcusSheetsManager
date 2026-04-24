using System.Security.Claims;
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

internal sealed class FullScan : IEndpoint
{
  public void MapEndpoint(IEndpointRouteBuilder app)
  {
    app.MapPost("manager/scan", async (
        ITokenAuthenticator auth,
        ICommandHandler<FullScanCommand> handler,
        HttpContext ctx,
        CancellationToken cancellationToken) =>
    {
      if (!auth.ValidateFromContext(ctx, new Claim("NsmAdmin", "true")))
      {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
      }

      Result result = await handler.Handle(new FullScanCommand(), cancellationToken);
      return result.Match(() => Results.Ok(), CustomResults.Problem);
    })
    .WithTags(Tags.Manager);
  }
}
