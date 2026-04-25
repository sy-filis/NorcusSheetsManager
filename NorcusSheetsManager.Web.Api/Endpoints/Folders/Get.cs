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
        return Results.Unauthorized();
      }

      Result<IReadOnlyList<string>> result = await handler.Handle(new GetFoldersQuery(), cancellationToken);
      return result.Match(Results.Ok, CustomResults.Problem);
    })
    .WithTags(Tags.Folders)
    .WithSummary("List sheet folders")
    .WithDescription("Returns the list of top-level sheet folders. Folders whose name begins with a dot are excluded.")
    .Produces<IReadOnlyList<string>>(StatusCodes.Status200OK)
    .WithResponseExample(StatusCodes.Status200OK, new[] { "teri", "jakub", "shared" })
    .ProducesProblem(StatusCodes.Status401Unauthorized);
  }
}
