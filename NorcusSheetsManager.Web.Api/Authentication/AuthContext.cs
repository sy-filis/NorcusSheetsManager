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
}
