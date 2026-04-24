using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NorcusSheetsManager.NameCorrector;

namespace NorcusSheetsManager.API.Resources;

internal static class MasterResource
{
  public static void MapEndpoints(IEndpointRouteBuilder app)
  {
    app.MapGet("/api/v1/folders", (ITokenAuthenticator auth, Corrector corrector, HttpContext ctx) =>
    {
      if (!auth.ValidateFromContext(ctx))
      {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
      }

      IEnumerable<string> folders = Directory.GetDirectories(corrector.BaseSheetsFolder)
                .Select(d => Path.GetFileName(d))
                .Where(d => !string.IsNullOrEmpty(d) && !d.StartsWith("."));

      return Results.Json(folders);
    });
  }
}
