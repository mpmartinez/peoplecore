using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using PeopleCore.API.Accounts;
using PeopleCore.API.Extensions;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Infrastructure.Identity;

namespace PeopleCore.API.Controllers.Auth;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IConfiguration _configuration;
    private readonly IRolePermissionReader _permissions;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IConfiguration configuration,
        IRolePermissionReader permissions)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _configuration = configuration;
        _permissions = permissions;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        // A deactivated account gets the same answer as a wrong password, so the response never
        // confirms that an email belongs to an account. Checked before the password so a
        // deactivated account's lockout counter is left alone.
        if (user is null || !user.IsActive) return Unauthorized(new { message = "Invalid credentials." });

        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded) return Unauthorized(new { message = "Invalid credentials." });

        return Ok(await IssueTokenAsync(user));
    }

    // Self-service only: the account is the one the bearer token names, never one the body picks.
    // The current password goes through CheckPasswordSignInAsync rather than straight into
    // ChangePasswordAsync, because only the former counts failures towards lockout - without it a
    // stolen token could guess the current password as many times as it liked.
    [Authorize]
    [AllowDuringPasswordChange]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        if (string.IsNullOrEmpty(request.CurrentPassword) || string.IsNullOrEmpty(request.NewPassword))
            return PasswordProblem("Enter both your current password and a new password.");
        if (request.NewPassword == request.CurrentPassword)
            return PasswordProblem("Your new password must be different from your current password.");

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = userId is null ? null : await _userManager.FindByIdAsync(userId);
        if (user is null) return Unauthorized();

        var check = await _signInManager.CheckPasswordSignInAsync(user, request.CurrentPassword, lockoutOnFailure: true);
        if (check.IsLockedOut)
            return PasswordProblem("Too many incorrect attempts. Your account is locked for a few minutes; try again later.");
        if (!check.Succeeded)
            return PasswordProblem("Your current password is incorrect.");

        var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
            return PasswordProblem(string.Join(" ", result.Errors.Select(e => e.Description)));

        // ChangePasswordAsync has just replaced the security stamp, revoking the token this request
        // came with. Hand back a fresh one so the user stays signed in.
        if (user.MustChangePassword)
        {
            user.MustChangePassword = false;
            var cleared = await _userManager.UpdateAsync(user);
            if (!cleared.Succeeded)
                return PasswordProblem(string.Join(" ", cleared.Errors.Select(e => e.Description)));
        }

        return Ok(await IssueTokenAsync(user));
    }

    private BadRequestObjectResult PasswordProblem(string detail) =>
        BadRequest(new ProblemDetails { Title = "Password not changed", Detail = detail, Status = StatusCodes.Status400BadRequest });

    private async Task<AuthTokenResponse> IssueTokenAsync(ApplicationUser user)
    {
        var roles = await _userManager.GetRolesAsync(user);
        var permissions = await _permissions.GetPermissionsAsync(roles);
        return new AuthTokenResponse(GenerateJwtToken(user, roles, permissions), user.Email, roles.ToList(), user.MustChangePassword);
    }

    private string GenerateJwtToken(ApplicationUser user, IList<string> roles, IReadOnlyList<string> permissions)
    {
        var key = new SymmetricSecurityKey(ServiceExtensions.ResolveJwtSigningKey(_configuration));
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Email, user.Email!),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(AccountClaims.SecurityStamp, user.SecurityStamp ?? string.Empty)
        };
        if (user.EmployeeId.HasValue)
            claims.Add(new Claim("employee_id", user.EmployeeId.Value.ToString()));
        if (user.MustChangePassword)
            claims.Add(new Claim(AccountClaims.MustChangePassword, "true"));
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        // Access is decided by these, not by the role names above - which stay only so the client can
        // show them. Every token gets perm_v, even one granting nothing, so AccountTokenValidator can
        // tell a token from before permissions from one that simply has none.
        claims.AddRange(permissions.Select(p => new Claim(Permissions.ClaimType, p)));
        claims.Add(new Claim(Permissions.VersionClaimType, Permissions.CurrentVersion));

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(Convert.ToDouble(_configuration["Jwt:ExpiryMinutes"])),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public record LoginRequest(string Email, string Password);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record AuthTokenResponse(string Token, string? Email, IReadOnlyList<string> Roles, bool MustChangePassword);
