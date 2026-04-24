using System.Security.Claims;
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
      if (!auth.ValidateFromContext(ctx, new Claim("NsmAdmin", "true")))
      {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
      }

      var command = new AutoFixInvalidNamesCommand { IsAdmin = true, UserId = Guid.Empty };
      Result<AutoFixInvalidNamesResponse> result = await handler.Handle(command, cancellationToken);
      return result.Match(Results.Ok, CustomResults.Problem);
    })
    .WithTags(Tags.Corrector);
  }
}
