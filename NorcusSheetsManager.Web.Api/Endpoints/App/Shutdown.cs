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
      IResult? authFailure = auth.RequireAdmin(ctx);
      if (authFailure is not null)
      {
        return authFailure;
      }

      Result result = await handler.Handle(new ShutdownCommand(), cancellationToken);
      return result.Match(() => Results.Ok(), CustomResults.Problem);
    })
    .WithTags(Tags.App)
    .WithSummary("Shut the application down")
    .WithDescription("Stops the host via IHostApplicationLifetime.StopApplication. Mirrors the X/T interactive console command — useful when running with --no-console where keyboard control isn't available. Admin only.")
    .Produces(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status401Unauthorized)
    .ProducesProblem(StatusCodes.Status403Forbidden);
  }
}
