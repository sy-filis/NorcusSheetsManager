using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NorcusSheetsManager.Application.Abstractions.Messaging;
using NorcusSheetsManager.Application.Corrector.GetInvalidNamesCount;
using NorcusSheetsManager.SharedKernel;
using NorcusSheetsManager.Web.Api.Authentication;
using NorcusSheetsManager.Web.Api.Extensions;
using NorcusSheetsManager.Web.Api.Infrastructure;

namespace NorcusSheetsManager.Web.Api.Endpoints.Corrector;

internal sealed class GetInvalidNamesCount : IEndpoint
{
  public sealed record Response(int Count);

  public void MapEndpoint(IEndpointRouteBuilder app)
  {
    app.MapGet("corrector/count", (
        ITokenAuthenticator auth,
        IQueryHandler<GetInvalidNamesCountQuery, int> handler,
        HttpContext ctx,
        CancellationToken cancellationToken)
            => HandleAsync(null, auth, handler, ctx, cancellationToken))
      .WithTags(Tags.Corrector)
      .WithSummary("Count files with invalid names")
      .WithDescription("Returns the total number of incorrectly named files across every folder. Non-admin callers receive only the count for their own folder, resolved from the JWT 'uuid' claim.")
      .Produces<Response>(StatusCodes.Status200OK)
      .WithResponseExample(StatusCodes.Status200OK, new Response(7))
      .ProducesProblem(StatusCodes.Status401Unauthorized);

    app.MapGet("corrector/{folder}/count", (
        string folder,
        ITokenAuthenticator auth,
        IQueryHandler<GetInvalidNamesCountQuery, int> handler,
        HttpContext ctx,
        CancellationToken cancellationToken)
            => HandleAsync(folder, auth, handler, ctx, cancellationToken))
      .WithTags(Tags.Corrector)
      .WithSummary("Count invalid names in a folder")
      .WithDescription("Returns the count of incorrectly named files in the given folder. Non-admin callers requesting a folder other than their own receive HTTP 403.")
      .Produces<Response>(StatusCodes.Status200OK)
      .WithResponseExample(StatusCodes.Status200OK, new Response(3))
      .ProducesProblem(StatusCodes.Status401Unauthorized);
  }

  private static async Task<IResult> HandleAsync(
      string? folder,
      ITokenAuthenticator auth,
      IQueryHandler<GetInvalidNamesCountQuery, int> handler,
      HttpContext ctx,
      CancellationToken cancellationToken)
  {
    AuthContext authCtx = auth.GetAuthContext(ctx);
    if (!authCtx.IsAuthenticated)
    {
      return Results.Unauthorized();
    }

    var query = new GetInvalidNamesCountQuery
    {
      Folder = folder,
      IsAdmin = authCtx.IsAdmin,
      UserId = authCtx.UserId,
    };

    Result<int> result = await handler.Handle(query, cancellationToken);
    return result.Match(count => Results.Ok(new Response(count)), CustomResults.Problem);
  }
}
