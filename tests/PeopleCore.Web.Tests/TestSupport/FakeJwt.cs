using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace PeopleCore.Web.Tests.TestSupport;

/// <summary>
/// Builds an unsigned JWT. The client only ever reads a token's claims - the API is what verifies
/// the signature - so signing one here would be ceremony that tests nothing.
/// </summary>
public static class FakeJwt
{
    public static string For(string email, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.Email, email) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var token = new JwtSecurityToken(claims: claims, expires: DateTime.UtcNow.AddHours(1));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
