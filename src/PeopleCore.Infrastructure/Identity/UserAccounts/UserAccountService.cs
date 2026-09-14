using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Common.DTOs;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Identity.UserAccounts;

public class UserAccountService : IUserAccountService
{
    public const string AdminRole = "Admin";
    public const int MaxPageSize = 100;

    // Identity's own column width for Email and UserName.
    private const int EmailMaxLength = 256;

    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public UserAccountService(AppDbContext db, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
    {
        _db = db;
        _userManager = userManager;
        _roleManager = roleManager;
    }

    public async Task<PagedResult<UserAccountDto>> ListAsync(string? search, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = _db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            // Escaped, so a search for "a_b" means that text rather than "a, any character, b".
            var pattern = "%" + search.Trim().Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%";
            query = query.Where(u =>
                EF.Functions.ILike(u.Email!, pattern, @"\") ||
                EF.Functions.ILike(u.FirstName!, pattern, @"\") ||
                EF.Functions.ILike(u.LastName!, pattern, @"\"));
        }

        var total = await query.CountAsync(ct);
        var users = await query
            .OrderBy(u => u.Email)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return PagedResult<UserAccountDto>.Create(await ToDtosAsync(users, ct), total, page, pageSize);
    }

    public async Task<UserAccountDto?> GetAsync(string id, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        return user is null ? null : (await ToDtosAsync([user], ct))[0];
    }

    public async Task<IReadOnlyList<string>> GetRoleNamesAsync(CancellationToken ct = default)
        => await _roleManager.Roles.Select(r => r.Name!).OrderBy(n => n).ToListAsync(ct);

    public async Task<UserAccountResult> CreateAsync(CreateUserAccountRequest request, CancellationToken ct = default)
    {
        var email = request.Email?.Trim();
        var firstName = request.FirstName?.Trim();
        var lastName = request.LastName?.Trim();

        var problem = ValidateEmail(email) ?? ValidateName(firstName, "first name") ?? ValidateName(lastName, "last name");
        if (problem is not null) return UserAccountResult.Invalid(problem);
        if (string.IsNullOrEmpty(request.Password)) return UserAccountResult.Invalid("Enter a password.");

        var (roles, roleProblem) = await ResolveRolesAsync(request.Roles, ct);
        if (roleProblem is not null) return UserAccountResult.Invalid(roleProblem);

        if (await _userManager.FindByEmailAsync(email!) is not null || await _userManager.FindByNameAsync(email!) is not null)
            return UserAccountResult.Conflict($"An account with the email {email} already exists.");

        var linkProblem = await CheckEmployeeLinkAsync(request.EmployeeId, userId: null, ct);
        if (linkProblem is not null) return linkProblem;

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = firstName,
            LastName = lastName,
            EmployeeId = request.EmployeeId
        };

        // One transaction, so an account whose roles fail to save does not survive without them.
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        var created = await _userManager.CreateAsync(user, request.Password);
        if (!created.Succeeded) return UserAccountResult.Invalid(Describe(created));

        if (roles.Count > 0)
        {
            var added = await _userManager.AddToRolesAsync(user, roles);
            if (!added.Succeeded) return UserAccountResult.Invalid(Describe(added));
        }

        await transaction.CommitAsync(ct);
        return UserAccountResult.Ok((await ToDtosAsync([user], ct))[0]);
    }

    public async Task<UserAccountResult> UpdateAsync(string id, UpdateUserAccountRequest request, string callerId, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null) return UserAccountResult.NotFound();

        var email = request.Email?.Trim();
        var firstName = request.FirstName?.Trim();
        var lastName = request.LastName?.Trim();

        var problem = ValidateEmail(email) ?? ValidateName(firstName, "first name") ?? ValidateName(lastName, "last name");
        if (problem is not null) return UserAccountResult.Invalid(problem);

        var (roles, roleProblem) = await ResolveRolesAsync(request.Roles, ct);
        if (roleProblem is not null) return UserAccountResult.Invalid(roleProblem);

        var currentRoles = await _userManager.GetRolesAsync(user);
        var losesAdmin = currentRoles.Contains(AdminRole) && !roles.Contains(AdminRole);
        if (losesAdmin)
        {
            // Without these, the Users page could be locked away from everyone - by the caller
            // demoting themselves, or by demoting the last Admin there is.
            if (user.Id == callerId)
                return UserAccountResult.Invalid("You cannot remove the Admin role from the account you are signed in with.");
            if (await IsOnlyAdminAsync())
                return UserAccountResult.Invalid("This is the only Admin account. Give another account the Admin role first.");
        }

        if (!string.Equals(email, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            var holder = await _userManager.FindByEmailAsync(email!) ?? await _userManager.FindByNameAsync(email!);
            if (holder is not null && holder.Id != user.Id)
                return UserAccountResult.Conflict($"An account with the email {email} already exists.");
        }

        var linkProblem = await CheckEmployeeLinkAsync(request.EmployeeId, user.Id, ct);
        if (linkProblem is not null) return linkProblem;

        // The email is the sign-in username, so the two always move together.
        user.Email = email;
        user.UserName = email;
        user.FirstName = firstName;
        user.LastName = lastName;
        user.EmployeeId = request.EmployeeId;

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        var updated = await _userManager.UpdateAsync(user);
        if (!updated.Succeeded) return UserAccountResult.Invalid(Describe(updated));

        var toRemove = currentRoles.Except(roles).ToList();
        if (toRemove.Count > 0)
        {
            var removed = await _userManager.RemoveFromRolesAsync(user, toRemove);
            if (!removed.Succeeded) return UserAccountResult.Invalid(Describe(removed));
        }

        var toAdd = roles.Except(currentRoles).ToList();
        if (toAdd.Count > 0)
        {
            var added = await _userManager.AddToRolesAsync(user, toAdd);
            if (!added.Succeeded) return UserAccountResult.Invalid(Describe(added));
        }

        if (!string.IsNullOrEmpty(request.NewPassword))
        {
            // An administrator's reset: no current password is asked for. The reset token is spent
            // on the spot, never handed out.
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var reset = await _userManager.ResetPasswordAsync(user, token, request.NewPassword);
            if (!reset.Succeeded) return UserAccountResult.Invalid(Describe(reset));
        }

        await transaction.CommitAsync(ct);
        return UserAccountResult.Ok((await ToDtosAsync([user], ct))[0]);
    }

    public async Task<UserAccountResult> DeleteAsync(string id, string callerId, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null) return UserAccountResult.NotFound();

        if (user.Id == callerId)
            return UserAccountResult.Invalid("You cannot delete the account you are signed in with.");
        if (await _userManager.IsInRoleAsync(user, AdminRole) && await IsOnlyAdminAsync())
            return UserAccountResult.Invalid("This is the only Admin account. Give another account the Admin role first.");

        // The employee record, if any, stays: it is HR's, and only the sign-in goes.
        var deleted = await _userManager.DeleteAsync(user);
        return deleted.Succeeded ? UserAccountResult.Ok(null) : UserAccountResult.Invalid(Describe(deleted));
    }

    private async Task<bool> IsOnlyAdminAsync() => (await _userManager.GetUsersInRoleAsync(AdminRole)).Count <= 1;

    /// <summary>
    /// The requested roles under their stored names, without duplicates, or the first one that does
    /// not exist. Matching ignores case, so "admin" is read as Admin rather than refused.
    /// </summary>
    private async Task<(IReadOnlyList<string> Roles, string? Problem)> ResolveRolesAsync(IReadOnlyList<string>? requested, CancellationToken ct)
    {
        if (requested is null || requested.Count == 0) return ([], null);

        var known = await GetRoleNamesAsync(ct);
        var resolved = new List<string>();
        foreach (var name in requested.Select(r => r?.Trim() ?? ""))
        {
            var match = known.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
            if (match is null) return ([], $"There is no role named \"{name}\".");
            if (!resolved.Contains(match)) resolved.Add(match);
        }

        return (resolved, null);
    }

    private async Task<UserAccountResult?> CheckEmployeeLinkAsync(Guid? employeeId, string? userId, CancellationToken ct)
    {
        if (employeeId is not { } id) return null;

        if (!await _db.Employees.AnyAsync(e => e.Id == id, ct))
            return UserAccountResult.Invalid("The selected employee could not be found.");

        // One account per employee: the employee_id claim is what self-service and manager scoping
        // key on, so two accounts sharing it would each see the other's pay, leave and attendance.
        if (await _db.Users.AnyAsync(u => u.EmployeeId == id && u.Id != userId, ct))
            return UserAccountResult.Conflict("The selected employee is already linked to another account.");

        return null;
    }

    private async Task<IReadOnlyList<UserAccountDto>> ToDtosAsync(IReadOnlyList<ApplicationUser> users, CancellationToken ct)
    {
        var userIds = users.Select(u => u.Id).ToList();
        var roles = await (
                from userRole in _db.UserRoles
                join role in _db.Roles on userRole.RoleId equals role.Id
                where userIds.Contains(userRole.UserId)
                select new { userRole.UserId, role.Name })
            .ToListAsync(ct);

        var employeeIds = users.Where(u => u.EmployeeId.HasValue).Select(u => u.EmployeeId!.Value).Distinct().ToList();
        var employees = await _db.Employees
            .Where(e => employeeIds.Contains(e.Id))
            .Select(e => new { e.Id, e.EmployeeNumber, e.FirstName, e.LastName })
            .ToDictionaryAsync(e => e.Id, ct);

        return users.Select(u =>
        {
            var employee = u.EmployeeId is { } eid && employees.TryGetValue(eid, out var found) ? found : null;
            return new UserAccountDto(
                u.Id,
                u.Email ?? "",
                u.FirstName,
                u.LastName,
                roles.Where(r => r.UserId == u.Id).Select(r => r.Name!).OrderBy(n => n).ToList(),
                u.EmployeeId,
                employee?.EmployeeNumber,
                employee is null ? null : $"{employee.FirstName} {employee.LastName}");
        }).ToList();
    }

    private static string? ValidateEmail(string? email)
    {
        if (string.IsNullOrEmpty(email)) return "Enter an email address.";
        if (email.Length > EmailMaxLength) return $"The email address must be {EmailMaxLength} characters or fewer.";
        if (!MailAddress.TryCreate(email, out var address) || address.Address != email)
            return $"\"{email}\" is not a valid email address.";
        return null;
    }

    private static string? ValidateName(string? name, string label) => name switch
    {
        null or "" => $"Enter a {label}.",
        { Length: > ApplicationUser.NameMaxLength } =>
            $"{char.ToUpperInvariant(label[0])}{label[1..]} must be {ApplicationUser.NameMaxLength} characters or fewer.",
        _ => null
    };

    private static string Describe(IdentityResult result) => string.Join(" ", result.Errors.Select(e => e.Description));
}
