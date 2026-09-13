using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Infrastructure.Identity;

namespace PeopleCore.API.Controllers.Account;

/// <summary>
/// The signed-in account's own profile. There is no id in the route: the account is always the
/// one the bearer token names. Deliberately not under api/auth, whose 401s the web client treats
/// as a failed sign-in rather than an expired session.
/// </summary>
[ApiController]
[Authorize]
[Route("api/profile")]
public class ProfileController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmployeeRepository _employees;

    public ProfileController(UserManager<ApplicationUser> userManager, IEmployeeRepository employees)
    {
        _userManager = userManager;
        _employees = employees;
    }

    [HttpGet]
    public async Task<ActionResult<UserProfileDto>> Get(CancellationToken ct)
    {
        var user = await FindCallerAsync();
        if (user is null) return Unauthorized();

        return Ok(await ToProfileAsync(user, ct));
    }

    // Names only. The email is the sign-in username, so changing it is not a profile edit.
    [HttpPut]
    public async Task<ActionResult<UserProfileDto>> Update([FromBody] UpdateProfileRequest request, CancellationToken ct)
    {
        var user = await FindCallerAsync();
        if (user is null) return Unauthorized();

        var firstName = request.FirstName?.Trim();
        var lastName = request.LastName?.Trim();
        var problem = Validate(firstName, "first name") ?? Validate(lastName, "last name");
        if (problem is not null) return ProfileProblem(problem);

        user.FirstName = firstName;
        user.LastName = lastName;
        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
            return ProfileProblem(string.Join(" ", result.Errors.Select(e => e.Description)));

        return Ok(await ToProfileAsync(user, ct));
    }

    private async Task<ApplicationUser?> FindCallerAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return userId is null ? null : await _userManager.FindByIdAsync(userId);
    }

    // An account that has never saved names borrows them from its employee record, so a linked
    // employee opens a filled-in form rather than a blank one.
    private async Task<UserProfileDto> ToProfileAsync(ApplicationUser user, CancellationToken ct)
    {
        if (user.FirstName is null && user.LastName is null && user.EmployeeId is { } employeeId)
        {
            var employee = await _employees.GetByIdAsync(employeeId, ct);
            if (employee is not null)
                return new UserProfileDto(employee.FirstName, employee.LastName, user.Email);
        }

        return new UserProfileDto(user.FirstName, user.LastName, user.Email);
    }

    private static string? Validate(string? name, string label) => name switch
    {
        null or "" => $"Enter your {label}.",
        { Length: > ApplicationUser.NameMaxLength } =>
            $"{char.ToUpperInvariant(label[0])}{label[1..]} must be {ApplicationUser.NameMaxLength} characters or fewer.",
        _ => null
    };

    private BadRequestObjectResult ProfileProblem(string detail) =>
        BadRequest(new ProblemDetails { Title = "Profile not saved", Detail = detail, Status = StatusCodes.Status400BadRequest });
}

public record UserProfileDto(string? FirstName, string? LastName, string? Email);

public record UpdateProfileRequest(string? FirstName, string? LastName);
