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
using PeopleCore.Infrastructure.Email;
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
    private readonly IEmailSender _email;
    private readonly IEmailSettingsStore _mailSettings;
    private readonly IResetRequestThrottle _throttle;
    private readonly ILogger<AuthController> _log;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IConfiguration configuration,
        IRolePermissionReader permissions,
        IEmailSender email,
        IEmailSettingsStore mailSettings,
        IResetRequestThrottle throttle,
        ILogger<AuthController> log)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _configuration = configuration;
        _permissions = permissions;
        _email = email;
        _mailSettings = mailSettings;
        _throttle = throttle;
        _log = log;
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

    // Every outcome answers the same way. An answer that varied - "no such account", "too many
    // tries" - would turn this endpoint into a way to find out which addresses are real.
    private const string ResetRequested = "If that address has an account, we've sent a link to reset the password.";

    [AllowAnonymous]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken ct)
    {
        var email = (request.Email ?? string.Empty).Trim();
        var caller = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (email.Length == 0 || !_throttle.TryRequest(email, caller))
        {
            _log.LogInformation("Password reset not sent: empty address or rate limit reached.");
            return Ok(new { message = ResetRequested });
        }

        var user = await _userManager.FindByEmailAsync(email);
        if (user is null || !user.IsActive)
        {
            _log.LogInformation("Password reset not sent: no active account for the address given.");
            return Ok(new { message = ResetRequested });
        }

        // From here on the request's token is ignored: a closed tab would otherwise lose a real user's
        // link. The SMTP client's own timeout still bounds how long this can take.
        var account = await _mailSettings.GetAccountAsync(CancellationToken.None);
        if (account is null)
        {
            _log.LogWarning("Password reset not sent for {UserId}: no email account is configured.", user.Id);
            return Ok(new { message = ResetRequested });
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var link = $"{account.AppBaseUrl.TrimEnd('/')}/reset-password" +
                   $"?email={Uri.EscapeDataString(user.Email!)}&token={Uri.EscapeDataString(token)}";

        try
        {
            await _email.SendAsync(PasswordResetMail.For(user.Email!, user.FirstName ?? string.Empty, link), CancellationToken.None);
            _log.LogInformation("Password reset link sent to {UserId}.", user.Id);
        }
        catch (Exception ex)
        {
            // The user is told nothing either way; an administrator finds out here.
            _log.LogError(ex, "Sending the password reset link to {UserId} failed.", user.Id);
        }

        return Ok(new { message = ResetRequested });
    }

    [AllowAnonymous]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        var user = await _userManager.FindByEmailAsync((request.Email ?? string.Empty).Trim());
        // An address with no account is answered like a stale link, so a link is not a way to ask
        // whether an account exists either.
        if (user is null || !user.IsActive || string.IsNullOrEmpty(request.Token)) return StaleLink();

        var result = await _userManager.ResetPasswordAsync(user, request.Token, request.NewPassword ?? string.Empty);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == "InvalidToken")) return StaleLink();
            return PasswordProblem(string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        // ResetPasswordAsync has replaced the security stamp, so every token issued before now is
        // dead. What is left is the state an administrator's reset also clears.
        await _userManager.SetLockoutEndDateAsync(user, null);
        await _userManager.ResetAccessFailedCountAsync(user);

        if (user.MustChangePassword)
        {
            user.MustChangePassword = false;
            await _userManager.UpdateAsync(user);
        }

        _log.LogInformation("Password reset completed for {UserId}.", user.Id);
        return Ok(new { message = "Your password has been changed. Sign in with your new password." });
    }

    /// <summary>Lets the login page hide the link rather than send people to a page that cannot deliver.</summary>
    [AllowAnonymous]
    [HttpGet("password-reset-available")]
    public async Task<IActionResult> PasswordResetAvailable(CancellationToken ct) =>
        Ok(new { available = await _mailSettings.GetAccountAsync(ct) is not null });

    private BadRequestObjectResult StaleLink() =>
        BadRequest(new ProblemDetails
        {
            Title = "Link no longer valid",
            Detail = "This link has expired or has already been used. Ask for a new one.",
            Status = StatusCodes.Status400BadRequest
        });

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

public record ForgotPasswordRequest(string Email);

public record ResetPasswordRequest(string Email, string Token, string NewPassword);

public record AuthTokenResponse(string Token, string? Email, IReadOnlyList<string> Roles, bool MustChangePassword);
