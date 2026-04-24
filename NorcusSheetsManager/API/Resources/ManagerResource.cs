using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace NorcusSheetsManager.API.Resources;

internal static class ManagerResource
{
  public static void MapEndpoints(IEndpointRouteBuilder app)
  {
    RouteGroupBuilder group = app.MapGroup("/api/v1/manager");

    group.MapPost("/scan", (ITokenAuthenticator auth, Manager manager, HttpContext ctx) =>
    {
      if (!auth.ValidateFromContext(ctx, new Claim("NsmAdmin", "true")))
      {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
      }

      _ = System.Threading.Tasks.Task.Run(() => manager.FullScan());
      return Results.Ok();
    });

    group.MapPost("/deep-scan", (ITokenAuthenticator auth, Manager manager, HttpContext ctx) =>
    {
      if (!auth.ValidateFromContext(ctx, new Claim("NsmAdmin", "true")))
      {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
      }

      _ = System.Threading.Tasks.Task.Run(() => manager.DeepScan());
      return Results.Ok();
    });

    group.MapPost("/convert-all", (ITokenAuthenticator auth, Manager manager, HttpContext ctx) =>
    {
      if (!auth.ValidateFromContext(ctx, new Claim("NsmAdmin", "true")))
      {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
      }

      _ = System.Threading.Tasks.Task.Run(() => manager.ForceConvertAll());
      return Results.Ok();
    });
  }
}
