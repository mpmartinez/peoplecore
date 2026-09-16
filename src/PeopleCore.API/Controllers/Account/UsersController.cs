using System.Net.Mail;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.API.Accounts;
using PeopleCore.API.Authorization;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Infrastructure.Identity;

namespace PeopleCore.API.Controllers.Account;

/// <summary>
/// Sign-in accounts: creating them, the roles they hold, the employee each belongs to, and whether
/// they may sign in at all. Whether the caller may make a given change is
/// <see cref="AccountManagementPolicy"/>'s decision; this controller asks, then acts. Every change
/// replaces the account's security stamp, which revokes the tokens that account already holds.
/// </summary>
[ApiController]
[RequirePermission(Permissions.UsersManage)]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private const int MaxPageSize = 100;

    private readonly UserManager<ApplicationUser> _users;
    private readonly IUserAccountDirectory _directory;
    private readonly IEmployeeRepository _employees;
    private readonly IRoleCatalog _roles;

    public UsersController(
        UserManager<ApplicationUser> users, IUserAccountDirectory directory, IEmployeeRepository employees, IRoleCatalog roles)
    {
        _users = users;
        _directory = directory;
        _employees = employees;
        _roles = roles;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<UserAccountDto>>> List(
        [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var (rows, total) = await _directory.SearchAsync(search, page, pageSize, ct);
        var catalog = await CatalogAsync(ct);
        return Ok(PagedResult<UserAccountDto>.Create(rows.Select(row => ToDto(row, catalog)).ToList(), total, page, pageSize));
    }

    [HttpGet("assignable-roles")]
    public async Task<ActionResult<IReadOnlyList<AssignableRoleDto>>> AssignableRoles(CancellationToken ct) =>
        Ok(AccountManagementPolicy.AssignableRoles(Caller, await CatalogAsync(ct))
            .Select(role => new AssignableRoleDto(role.Name, role.Grantable, role.Reason))
            .ToList());

    [HttpGet("employee-links")]
    public async Task<ActionResult<IReadOnlyList<EmployeeLinkDto>>> EmployeeLinks(CancellationToken ct) =>
        Ok((await _directory.GetEmployeeLinksAsync(ct))
            .Select(link => new EmployeeLinkDto(link.EmployeeId, link.UserId, link.IsActive))
            .ToList());

    [HttpGet("{id}")]
    public Task<ActionResult<UserAccountDto>> Get(string id, CancellationToken ct) => AccountAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<CreatedUserAccountDto>> Create([FromBody] CreateUserAccountRequest request, CancellationToken ct)
    {
        var email = request.Email?.Trim();
        var firstName = request.FirstName?.Trim();
        var lastName = request.LastName?.Trim();

        var problem = ValidateEmail(email) ?? ValidateName(firstName, "first name") ?? ValidateName(lastName, "last name");
        if (problem is not null) return AccountProblem(problem);
        var catalog = await CatalogAsync(ct);
        if (NormalizeRoles(request.Roles, catalog, out var roles) is { } roleProblem) return AccountProblem(roleProblem);

        var decision = AccountManagementPolicy.CanCreate(Caller, roles, catalog);
        if (!decision.Allowed) return Refused(decision);

        if (await _users.FindByEmailAsync(email!) is not null)
            return AccountProblem($"An account with the email {email} already exists.");
        if (request.EmployeeId is { } employeeId && await ValidateEmployeeLinkAsync(employeeId, exceptUserId: null, ct) is { } linkProblem)
            return AccountProblem(linkProblem);

        var password = TemporaryPasswordGenerator.Generate();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = firstName,
            LastName = lastName,
            EmployeeId = request.EmployeeId,
            IsActive = true,
            MustChangePassword = true
        };

        var created = await _users.CreateAsync(user, password);
        if (!created.Succeeded) return AccountProblem(Describe(created));

        var granted = await _users.AddToRolesAsync(user, roles);
        if (!granted.Succeeded)
        {
            // An account with no roles is not what was asked for; do not leave one behind. But if
            // the delete itself fails, say so - an active, roleless account left silently behind
            // is worse than a verbose error.
            var deleted = await _users.DeleteAsync(user);
            return AccountProblem(deleted.Succeeded
                ? Describe(granted)
                : $"The account {email} was created but its roles could not be assigned ({Describe(granted)}), " +
                  $"and removing it failed ({Describe(deleted)}). Delete or fix it before trying again.");
        }

        var row = await _directory.GetAsync(user.Id, ct);
        return CreatedAtAction(nameof(Get), new { id = user.Id }, new CreatedUserAccountDto(ToDto(row!, catalog), password));
    }

    [HttpPut("{id}/roles")]
    public async Task<ActionResult<UserAccountDto>> SetRoles(string id, [FromBody] SetRolesRequest request, CancellationToken ct)
    {
        var catalog = await CatalogAsync(ct);
        if (NormalizeRoles(request.Roles, catalog, out var roles) is { } roleProblem) return AccountProblem(roleProblem);

        var user = await _users.FindByIdAsync(id);
        if (user is null) return NotFound();
        var held = await _users.GetRolesAsync(user);

        // Service can't be assigned, so the request never names it - but an account that already
        // holds it keeps it, and the policy must judge the roles the account will actually end up with.
        var keptService = held.Where(role => string.Equals(role, SeededRoles.Service, StringComparison.OrdinalIgnoreCase));
        var rolesAfter = roles.Concat(keptService).ToList();

        var decision = AccountManagementPolicy.CanSetRoles(Caller, Target(user, held), rolesAfter, catalog, await ActiveAdminsAsync(ct));
        if (!decision.Allowed) return Refused(decision);

        var toAdd = roles.Except(held, StringComparer.OrdinalIgnoreCase).ToList();
        var toRemove = held.Except(rolesAfter, StringComparer.OrdinalIgnoreCase).ToList();

        var added = false;
        if (toAdd.Count > 0)
        {
            var addResult = await _users.AddToRolesAsync(user, toAdd);
            if (!addResult.Succeeded) return AccountProblem(Describe(addResult));
            added = true;
        }

        if (toRemove.Count > 0 && await _users.RemoveFromRolesAsync(user, toRemove) is { Succeeded: false } removed)
        {
            // The add already went through even though the remove failed, so the account's roles
            // really did change; its existing tokens must be revoked even though this call fails.
            if (added) await _users.UpdateSecurityStampAsync(user);
            return AccountProblem(Describe(removed));
        }

        // Nothing was actually added or removed - e.g. the request just restates the roles the
        // account already holds. Replacing the stamp here would revoke every token the account
        // holds, including the caller's own if an admin re-saves their own row unchanged, for a
        // change that never happened.
        if (!added && toRemove.Count == 0) return await AccountAsync(user.Id, ct);

        return await SaveAndRevokeAsync(user, ct);
    }

    [HttpPut("{id}/employee")]
    public async Task<ActionResult<UserAccountDto>> LinkEmployee(string id, [FromBody] LinkEmployeeRequest request, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(id);
        if (user is null) return NotFound();

        var decision = AccountManagementPolicy.CanLinkEmployee(Caller, Target(user, await _users.GetRolesAsync(user)), await CatalogAsync(ct));
        if (!decision.Allowed) return Refused(decision);

        if (request.EmployeeId is { } employeeId && await ValidateEmployeeLinkAsync(employeeId, exceptUserId: id, ct) is { } linkProblem)
            return AccountProblem(linkProblem);

        // The employee_id claim in the account's tokens is now wrong, so they are revoked too.
        user.EmployeeId = request.EmployeeId;
        return await SaveAndRevokeAsync(user, ct);
    }

    [HttpPost("{id}/deactivate")]
    public Task<ActionResult<UserAccountDto>> Deactivate(string id, CancellationToken ct) => SetActiveAsync(id, active: false, ct);

    [HttpPost("{id}/reactivate")]
    public Task<ActionResult<UserAccountDto>> Reactivate(string id, CancellationToken ct) => SetActiveAsync(id, active: true, ct);

    [HttpPost("{id}/reset-password")]
    public async Task<ActionResult<TemporaryPasswordDto>> ResetPassword(string id, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(id);
        if (user is null) return NotFound();

        var decision = AccountManagementPolicy.CanResetPassword(Caller, Target(user, await _users.GetRolesAsync(user)), await CatalogAsync(ct));
        if (!decision.Allowed) return Refused(decision);

        // Fails closed: nothing about the account changes until the reset itself succeeds, so a
        // rejected reset leaves it exactly as locked (or not) as it was.
        var password = TemporaryPasswordGenerator.Generate();
        var token = await _users.GeneratePasswordResetTokenAsync(user);
        var reset = await _users.ResetPasswordAsync(user, token, password);
        if (!reset.Succeeded) return AccountProblem(Describe(reset));

        // A user locked out by failed guesses is usually why the reset was asked for. Set every
        // field before the one save, so the lockout clear, the flag and the new stamp are all
        // written together - and if that save fails, the old password is already gone, so a
        // retried reset still recovers the account.
        user.LockoutEnd = null;
        user.AccessFailedCount = 0;
        user.MustChangePassword = true;
        var saved = await _users.UpdateSecurityStampAsync(user);
        if (!saved.Succeeded) return AccountProblem(Describe(saved));

        return Ok(new TemporaryPasswordDto(password));
    }

    // --- Shared by every action ---------------------------------------------------------------

    // Roles and permissions come from the token. That is safe to trust because AccountTokenValidator
    // rejects any token issued before the account's roles, or a role's permissions, last changed.
    private AccountActor Caller => new(
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
        User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList(),
        User.FindAll(Permissions.ClaimType).Select(c => c.Value).ToList());

    private static AccountTarget Target(ApplicationUser user, IEnumerable<string> roles) =>
        new(user.Id, roles.ToList(), user.IsActive);

    private async Task<IReadOnlyList<RoleGrant>> CatalogAsync(CancellationToken ct) =>
        (await _roles.GetRolesAsync(ct)).Select(role => new RoleGrant(role.Name, role.Permissions)).ToList();

    private UserAccountDto ToDto(UserAccountRow row, IReadOnlyList<RoleGrant> catalog) => new(
        row.Id, row.Email, row.FirstName, row.LastName, row.Roles, row.IsActive, row.MustChangePassword,
        row.EmployeeId, row.EmployeeName,
        CanManage: AccountManagementPolicy.CanManage(Caller, new AccountTarget(row.Id, row.Roles, row.IsActive), catalog).Allowed);

    private async Task<ActionResult<UserAccountDto>> AccountAsync(string id, CancellationToken ct)
    {
        var row = await _directory.GetAsync(id, ct);
        return row is null ? NotFound() : Ok(ToDto(row, await CatalogAsync(ct)));
    }

    private async Task<ActionResult<UserAccountDto>> SetActiveAsync(string id, bool active, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(id);
        if (user is null) return NotFound();

        var target = Target(user, await _users.GetRolesAsync(user));
        var catalog = await CatalogAsync(ct);
        var decision = active
            ? AccountManagementPolicy.CanReactivate(Caller, target, catalog)
            : AccountManagementPolicy.CanDeactivate(Caller, target, catalog, await ActiveAdminsAsync(ct));
        if (!decision.Allowed) return Refused(decision);

        user.IsActive = active;
        return await SaveAndRevokeAsync(user, ct);
    }

    /// <summary>
    /// Replaces the security stamp, which revokes every token the account holds. UpdateSecurityStampAsync
    /// saves the whole user, so any field set just before it is written in the same update.
    /// </summary>
    private async Task<ActionResult<UserAccountDto>> SaveAndRevokeAsync(ApplicationUser user, CancellationToken ct)
    {
        var saved = await _users.UpdateSecurityStampAsync(user);
        if (!saved.Succeeded) return AccountProblem(Describe(saved));
        return await AccountAsync(user.Id, ct);
    }

    private Task<int> ActiveAdminsAsync(CancellationToken ct) => _directory.CountActiveInRoleAsync(SeededRoles.Admin, ct);

    /// <summary>
    /// The requested roles, spelt and ordered as the catalogue has them, always with Employee.
    /// Returns a problem naming the first role that isn't one an account can be given - an unknown
    /// name, or Service.
    /// </summary>
    private static string? NormalizeRoles(IReadOnlyList<string>? requested, IReadOnlyList<RoleGrant> catalog, out IReadOnlyList<string> roles)
    {
        requested ??= [];
        var assignable = catalog.Where(role => role.Name != SeededRoles.Service).Select(role => role.Name).ToList();

        roles = assignable
            .Where(name => name == SeededRoles.Employee || requested.Contains(name, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var unknown = requested.FirstOrDefault(role => !assignable.Contains(role, StringComparer.OrdinalIgnoreCase));
        return unknown is null ? null : $"{unknown} is not a role that can be assigned.";
    }

    private async Task<string?> ValidateEmployeeLinkAsync(Guid employeeId, string? exceptUserId, CancellationToken ct)
    {
        if (await _employees.GetByIdAsync(employeeId, ct) is null)
            return "That employee record does not exist.";

        var linkedTo = await _directory.FindUserLinkedToEmployeeAsync(employeeId, ct);
        return linkedTo is not null && linkedTo != exceptUserId ? "That employee already has a login." : null;
    }

    // The exact-match check rejects display-name forms such as "Ana <ana@company.test>", which
    // MailAddress accepts but which cannot be a sign-in username.
    private static string? ValidateEmail(string? email) =>
        !string.IsNullOrEmpty(email) && MailAddress.TryCreate(email, out var parsed) && parsed.Address == email
            ? null
            : "Enter a valid email address.";

    private static string? ValidateName(string? name, string label) => name switch
    {
        null or "" => $"Enter a {label}.",
        { Length: > ApplicationUser.NameMaxLength } =>
            $"{char.ToUpperInvariant(label[0])}{label[1..]} must be {ApplicationUser.NameMaxLength} characters or fewer.",
        _ => null
    };

    private static string Describe(IdentityResult result) => string.Join(" ", result.Errors.Select(e => e.Description));

    private BadRequestObjectResult AccountProblem(string detail) =>
        BadRequest(new ProblemDetails { Title = "Account not saved", Detail = detail, Status = StatusCodes.Status400BadRequest });

    private ObjectResult Refused(PolicyDecision decision) =>
        StatusCode(StatusCodes.Status403Forbidden,
            new ProblemDetails { Title = "Not allowed", Detail = decision.Reason, Status = StatusCodes.Status403Forbidden });
}

public record UserAccountDto(
    string Id,
    string Email,
    string? FirstName,
    string? LastName,
    IReadOnlyList<string> Roles,
    bool IsActive,
    bool MustChangePassword,
    Guid? EmployeeId,
    string? EmployeeName,
    bool CanManage);

public record CreateUserAccountRequest(string? Email, string? FirstName, string? LastName, Guid? EmployeeId, IReadOnlyList<string>? Roles);

public record CreatedUserAccountDto(UserAccountDto Account, string TemporaryPassword);

public record EmployeeLinkDto(Guid EmployeeId, string UserId, bool IsActive);

public record SetRolesRequest(IReadOnlyList<string>? Roles);

public record LinkEmployeeRequest(Guid? EmployeeId);

public record TemporaryPasswordDto(string TemporaryPassword);

public record AssignableRoleDto(string Name, bool Grantable, string? Reason);
