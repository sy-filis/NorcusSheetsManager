using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Logging;
using Microsoft.IdentityModel.Tokens;

namespace NorcusSheetsManager.Web.Api.Authentication;

internal sealed class JWTAuthenticator : ITokenAuthenticator
{
  private readonly string _key;
  private readonly ILogger<JWTAuthenticator> _logger;

  public JWTAuthenticator(string secureKey, ILogger<JWTAuthenticator> logger, IHostEnvironment hostEnvironment)
  {
    _key = secureKey;
    _logger = logger;
    IdentityModelEventSource.ShowPII = hostEnvironment.IsDevelopment();
    if (string.IsNullOrEmpty(secureKey))
    {
      _logger.LogWarning("Secure key was not set. All requests will be accepted.");
    }
  }

  public bool IsTokenValid(string token) => ProcessToken(token).Valid;

  public string? GetClaimValue(HttpContext context, string claimType)
  {
    string? jwtToken = ExtractBearerToken(context);
    if (jwtToken is null)
    {
      return null;
    }
    return GetClaimValue(jwtToken, claimType);
  }

  public string? GetClaimValue(string token, string claimType)
  {
    ClaimsPrincipal? claims = ProcessToken(token).Claims;
    return claims?.Claims.FirstOrDefault(c => string.Equals(c.Type, claimType, StringComparison.OrdinalIgnoreCase))?.Value;
  }

  public bool ValidateFromContext(HttpContext context)
      => ValidateFromContext(context, []);

  public bool ValidateFromContext(HttpContext context, Claim requiredClaim)
      => ValidateFromContext(context, [requiredClaim]);

  public bool ValidateFromContext(HttpContext context, IEnumerable<Claim> requiredClaims)
  {
    if (string.IsNullOrEmpty(_key))
    {
      return true;
    }

    string? jwtToken = ExtractBearerToken(context);
    if (jwtToken is null)
    {
      return false;
    }

    (bool valid, ClaimsPrincipal? claims) = ProcessToken(jwtToken);
    return valid &&
           requiredClaims.All(requiredClaim =>
             claims?.FindFirst(c => c.Type == requiredClaim.Type && c.Value == requiredClaim.Value) is not null);
  }

  private static string? ExtractBearerToken(HttpContext context)
  {
    if (!context.Request.Headers.TryGetValue("Authorization", out StringValues authHeader))
    {
      return null;
    }
    string[] parts = authHeader.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    return parts.Length >= 2 ? parts[1] : null;
  }

  private (bool Valid, ClaimsPrincipal? Claims) ProcessToken(string token)
  {
    if (string.IsNullOrEmpty(_key))
    {
      return (true, null);
    }

    if (string.IsNullOrEmpty(token))
    {
      return (false, null);
    }

    TokenValidationParameters tokenValidationParameters = GetTokenValidationParameters();
    var jwtSecurityTokenHandler = new JwtSecurityTokenHandler();
    try
    {
      ClaimsPrincipal claims = jwtSecurityTokenHandler.ValidateToken(token, tokenValidationParameters, out SecurityToken validatedToken);
      return (true, claims);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error in JWT token validation.");
      return (false, null);
    }
  }

  private TokenValidationParameters GetTokenValidationParameters()
  {
    return new TokenValidationParameters
    {
      ValidateIssuer = false,
      ValidateAudience = false,
      ValidateLifetime = true,
      IssuerSigningKey = GetSymmetricSecurityKey(),
      LifetimeValidator = LifetimeValidator,
    };
  }

  private SymmetricSecurityKey GetSymmetricSecurityKey()
  {
    byte[] symmetricKey = Encoding.UTF8.GetBytes(_key);
    return new(symmetricKey);
  }

  private bool LifetimeValidator(DateTime? notBefore, DateTime? expires, SecurityToken securityToken, TokenValidationParameters validationParameters)
  {
    if (!validationParameters.ValidateLifetime)
    {
      return true;
    }
    DateTime now = DateTime.UtcNow;
    return now >= securityToken.ValidFrom && now <= securityToken.ValidTo;
  }
}
