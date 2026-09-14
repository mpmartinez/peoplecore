using Microsoft.EntityFrameworkCore;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Identity;

/// <inheritdoc cref="IUserAccountDirectory"/>
public class UserAccountDirectory : IUserAccountDirectory
{
    private readonly AppDbContext _db;

    public UserAccountDirectory(AppDbContext db) => _db = db;

    public async Task<(IReadOnlyList<UserAccountRow> Items, int TotalCount)> SearchAsync(
        string? search, int page, int pageSize, CancellationToken ct = default)
    {
        var users = _db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            users = users.Where(u =>
                EF.Functions.ILike(u.Email!, pattern) ||
                EF.Functions.ILike(u.FirstName!, pattern) ||
                EF.Functions.ILike(u.LastName!, pattern));
        }

        var total = await users.CountAsync(ct);
        var pageOfUsers = await users
            .OrderBy(u => u.Email)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (await ToRowsAsync(pageOfUsers, ct), total);
    }

    public async Task<UserAccountRow?> GetAsync(string userId, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, ct);
        return user is null ? null : (await ToRowsAsync([user], ct))[0];
    }

    public async Task<IReadOnlyList<EmployeeLinkRow>> GetEmployeeLinksAsync(CancellationToken ct = default) =>
        await _db.Users.AsNoTracking()
            .Where(u => u.EmployeeId != null)
            .Select(u => new EmployeeLinkRow(u.EmployeeId!.Value, u.Id, u.IsActive))
            .ToListAsync(ct);

    public Task<int> CountActiveInRoleAsync(string role, CancellationToken ct = default) =>
        (from user in _db.Users
         join userRole in _db.UserRoles on user.Id equals userRole.UserId
         join identityRole in _db.Roles on userRole.RoleId equals identityRole.Id
         where user.IsActive && identityRole.Name == role
         select user.Id)
        .Distinct()
        .CountAsync(ct);

    public Task<string?> FindUserLinkedToEmployeeAsync(Guid employeeId, CancellationToken ct = default) =>
        _db.Users
            .Where(u => u.EmployeeId == employeeId)
            .Select(u => (string?)u.Id)
            .SingleOrDefaultAsync(ct);

    // Two batched queries for a whole page - roles and employee names - rather than two per account.
    private async Task<IReadOnlyList<UserAccountRow>> ToRowsAsync(IReadOnlyList<ApplicationUser> users, CancellationToken ct)
    {
        var userIds = users.Select(u => u.Id).ToList();
        var roles = await (from userRole in _db.UserRoles
                           join identityRole in _db.Roles on userRole.RoleId equals identityRole.Id
                           where userIds.Contains(userRole.UserId)
                           select new { userRole.UserId, identityRole.Name })
                          .ToListAsync(ct);

        var employeeIds = users.Where(u => u.EmployeeId.HasValue).Select(u => u.EmployeeId!.Value).ToList();
        var employees = await _db.Employees.AsNoTracking()
            .Where(e => employeeIds.Contains(e.Id))
            .Select(e => new { e.Id, e.FirstName, e.LastName })
            .ToDictionaryAsync(e => e.Id, ct);

        return users.Select(u => new UserAccountRow(
                u.Id,
                u.Email ?? string.Empty,
                u.FirstName,
                u.LastName,
                roles.Where(r => r.UserId == u.Id && r.Name != null).Select(r => r.Name!).Order().ToList(),
                u.IsActive,
                u.MustChangePassword,
                u.EmployeeId,
                u.EmployeeId is { } id && employees.TryGetValue(id, out var employee)
                    ? $"{employee.FirstName} {employee.LastName}"
                    : null))
            .ToList();
    }
}
