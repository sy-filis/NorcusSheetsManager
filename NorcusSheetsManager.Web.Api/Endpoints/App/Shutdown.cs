using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NorcusSheetsManager.Application.Abstractions.Messaging;
using NorcusSheetsManager.Application.App.Shutdown;
using NorcusSheetsManager.SharedKernel;
using NorcusSheetsManager.Web.Api.Authentication;
using NorcusSheetsManager.Web.Api.Extensions;
using NorcusSheetsManager.Web.Api.Infrastructure;

namespace NorcusSheetsManager.Web.Api.Endpoints.App;

internal sealed class Shutdown : IEndpoint
{
  public void MapEndpoint(IEndpointRouteBuilder app)
  {
    app.MapPost("app/shutdown", async (
        ITokenAuthenticator auth,
        ICommandHandler<ShutdownCommand> handler,
        HttpContext ctx,
        CancellationToken cancellationToken) =>
    {
      if (!auth.ValidateFromContext(ctx, new Claim("NsmAdmin", "true")))
      {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
      }

      Result result = await handler.Handle(new ShutdownCommand(), cancellationToken);
      return result.Match(() => Results.Ok(), CustomResults.Problem);
    })
    .WithTags(Tags.App);
  }
}
