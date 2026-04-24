using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace NorcusSheetsManager.API;

public interface ITokenAuthenticator
{
  bool IsTokenValid(string token);
  string? GetClaimValue(HttpContext context, string claimType);
  string? GetClaimValue(string token, string claimType);
  bool ValidateFromContext(HttpContext context);
  bool ValidateFromContext(HttpContext context, Claim requiredClaim);
  bool ValidateFromContext(HttpContext context, IEnumerable<Claim> requiredClaims);
}
