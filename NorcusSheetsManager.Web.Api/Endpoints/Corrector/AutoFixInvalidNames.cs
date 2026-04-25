using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NorcusSheetsManager.Application.Abstractions.Messaging;
using NorcusSheetsManager.Application.Corrector.AutoFixInvalidNames;
using NorcusSheetsManager.SharedKernel;
using NorcusSheetsManager.Web.Api.Authentication;
using NorcusSheetsManager.Web.Api.Extensions;
using NorcusSheetsManager.Web.Api.Infrastructure;

namespace NorcusSheetsManager.Web.Api.Endpoints.Corrector;

internal sealed class AutoFixInvalidNames : IEndpoint
{
  public void MapEndpoint(IEndpointRouteBuilder app)
  {
    app.MapPost("corrector/auto-fix", async (
        ITokenAuthenticator auth,
        ICommandHandler<AutoFixInvalidNamesCommand, AutoFixInvalidNamesResponse> handler,
        HttpContext ctx,
        CancellationToken cancellationToken) =>
    {
      IResult? authFailure = auth.RequireAdmin(ctx);
      if (authFailure is not null)
      {
        return authFailure;
      }

      var command = new AutoFixInvalidNamesCommand { IsAdmin = true, UserId = Guid.Empty };
      Result<AutoFixInvalidNamesResponse> result = await handler.Handle(command, cancellationToken);
      return result.Match(Results.Ok, CustomResults.Problem);
    })
    .WithTags(Tags.Corrector)
    .WithSummary("Auto-apply the top suggestion to every invalid filename")
    .WithDescription("Walks every invalid filename and commits the closest-match suggestion for each. Mirrors the C/N interactive console command. Admin only.")
    .Produces<AutoFixInvalidNamesResponse>(StatusCodes.Status200OK)
    .WithResponseExample(StatusCodes.Status200OK, new AutoFixInvalidNamesResponse(
        TotalCount: 5,
        FixedCount: 4,
        Failures: ["teri/unknown_song-??.pdf"]))
    .ProducesProblem(StatusCodes.Status401Unauthorized)
    .ProducesProblem(StatusCodes.Status403Forbidden);
  }
}
