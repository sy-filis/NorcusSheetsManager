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
  public void MapEndpoint(IEndpointRouteBuilder app)
  {
    app.MapGet("corrector/count", (
        ITokenAuthenticator auth,
        IQueryHandler<GetInvalidNamesCountQuery, int> handler,
        HttpContext ctx,
        CancellationToken cancellationToken)
            => HandleAsync(null, auth, handler, ctx, cancellationToken))
      .WithTags(Tags.Corrector);

    app.MapGet("corrector/{folder}/count", (
        string folder,
        ITokenAuthenticator auth,
        IQueryHandler<GetInvalidNamesCountQuery, int> handler,
        HttpContext ctx,
        CancellationToken cancellationToken)
            => HandleAsync(folder, auth, handler, ctx, cancellationToken))
      .WithTags(Tags.Corrector);
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
    return result.Match(count => Results.Text(count.ToString()), CustomResults.Problem);
  }
}
