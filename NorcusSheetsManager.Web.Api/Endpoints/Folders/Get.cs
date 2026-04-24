using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NorcusSheetsManager.Application.Abstractions.Messaging;
using NorcusSheetsManager.Application.Folders.Get;
using NorcusSheetsManager.SharedKernel;
using NorcusSheetsManager.Web.Api.Authentication;
using NorcusSheetsManager.Web.Api.Extensions;
using NorcusSheetsManager.Web.Api.Infrastructure;

namespace NorcusSheetsManager.Web.Api.Endpoints.Folders;

internal sealed class Get : IEndpoint
{
  public void MapEndpoint(IEndpointRouteBuilder app)
  {
    app.MapGet("folders", async (
        ITokenAuthenticator auth,
        IQueryHandler<GetFoldersQuery, IReadOnlyList<string>> handler,
        HttpContext ctx,
        CancellationToken cancellationToken) =>
    {
      if (!auth.GetAuthContext(ctx).IsAuthenticated)
      {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
      }

      Result<IReadOnlyList<string>> result = await handler.Handle(new GetFoldersQuery(), cancellationToken);
      return result.Match(Results.Ok, CustomResults.Problem);
    })
    .WithTags(Tags.Folders);
  }
}
