using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace NorcusSheetsManager.Web.Api.Authentication;

public sealed record AuthContext(bool IsAuthenticated, bool IsAdmin, Guid UserId)
{
  public static AuthContext Empty { get; } = new(false, false, Guid.Empty);
}

public static class TokenAuthenticatorExtensions
{
  public static AuthContext GetAuthContext(this ITokenAuthenticator auth, HttpContext ctx)
  {
    if (!auth.ValidateFromContext(ctx))
    {
      return AuthContext.Empty;
    }

    bool isAdmin = auth.ValidateFromContext(ctx, new Claim("NsmAdmin", "true"));
    _ = Guid.TryParse(auth.GetClaimValue(ctx, "uuid"), out Guid userId);
    return new AuthContext(true, isAdmin, userId);
  }

  /// <summary>
  /// Returns 401 when the caller has no valid token, 403 when the token is valid but
  /// lacks the <c>NsmAdmin=true</c> claim, and <c>null</c> when the caller is an admin.
  /// Callers short-circuit their handler when this returns a non-null result.
  /// </summary>
  public static IResult? RequireAdmin(this ITokenAuthenticator auth, HttpContext ctx)
  {
    AuthContext authCtx = auth.GetAuthContext(ctx);
    if (!authCtx.IsAuthenticated)
    {
      return Results.Unauthorized();
    }
    if (!authCtx.IsAdmin)
    {
      return Results.StatusCode(StatusCodes.Status403Forbidden);
    }
    return null;
  }
}
