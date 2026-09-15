using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.API.Accounts;
using PeopleCore.API.Authorization;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Infrastructure.Identity;

namespace PeopleCore.API.Controllers.Account;

/// <summary>
/// Roles: what each allows, and who may change them. Whether the caller may make a change is
/// <see cref="RoleManagementPolicy"/>'s decision; this controller validates, asks, then writes.
/// Changing a role's permissions signs out everyone who holds it (see <see cref="IRoleEditor"/>).
/// </summary>
[ApiController]
[RequirePermission(Permissions.RolesManage)]
[Route("api/roles")]
public class RolesController : ControllerBase
{
    private const int NameMaxLength = 50;

    private readonly IRoleCatalog _catalog;
    private readonly IRoleEditor _editor;
    private readonly IUserAccountDirectory _directory;

    public RolesController(IRoleCatalog catalog, IRoleEditor editor, IUserAccountDirectory directory)
    {
        _catalog = catalog;
        _editor = editor;
        _directory = directory;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RoleDto>>> List(CancellationToken ct) =>
        Ok((await _catalog.GetRolesAsync(ct)).Select(ToDto).ToList());

    [HttpGet("{id}")]
    public async Task<ActionResult<RoleDto>> Get(string id, CancellationToken ct) =>
        await _catalog.GetAsync(id, ct) is { } role ? Ok(ToDto(role)) : NotFound();

    [HttpGet("permissions")]
    public ActionResult<IReadOnlyList<PermissionDto>> PermissionCatalogue() =>
        Ok(Permissions.All.Select(p => new PermissionDto(p.Key, p.Group, p.Label, p.Description)).ToList());

    [HttpPost]
    public async Task<ActionResult<RoleDto>> Create([FromBody] SaveRoleRequest request, CancellationToken ct)
    {
        if (Validate(request, out var name, out var description, out var permissions) is { } problem) return RoleProblem(problem);

        var decision = RoleManagementPolicy.CanCreate(Caller, permissions);
        if (!decision.Allowed) return Refused(decision);

        if (await _editor.NameTakenAsync(name, exceptRoleId: null, ct)) return RoleProblem($"A role called {name} already exists.");

        var id = await _editor.CreateAsync(name, description, permissions, ct);
        var created = await _catalog.GetAsync(id, ct);
        return CreatedAtAction(nameof(Get), new { id }, ToDto(created!));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<RoleDto>> Update(string id, [FromBody] SaveRoleRequest request, CancellationToken ct)
    {
        var role = await _catalog.GetAsync(id, ct);
        if (role is null) return NotFound();

        // The system-role and beyond-the-caller refusals come before validation, so a caller who may
        // not touch the role learns that rather than a complaint about the name they typed.
        var edit = RoleManagementPolicy.CanEdit(Caller, Snapshot(role));
        if (!edit.Allowed) return Refused(edit);

        if (Validate(request, out var name, out var description, out var permissions) is { } problem) return RoleProblem(problem);

        var decision = RoleManagementPolicy.CanUpdate(Caller, Snapshot(role), permissions, await CallerPermissionsAfterAsync(role, permissions, ct));
        if (!decision.Allowed) return Refused(decision);

        if (await _editor.NameTakenAsync(name, exceptRoleId: id, ct)) return RoleProblem($"A role called {name} already exists.");

        await _editor.UpdateAsync(id, name, description, permissions, ct);
        return Ok(ToDto((await _catalog.GetAsync(id, ct))!));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var role = await _catalog.GetAsync(id, ct);
        if (role is null) return NotFound();

        var decision = RoleManagementPolicy.CanDelete(Caller, Snapshot(role));
        if (!decision.Allowed) return Refused(decision);

        if (role.AccountCount > 0)
            return RoleProblem(role.AccountCount == 1
                ? $"1 account still has {role.Name} — remove it from them first."
                : $"{role.AccountCount} accounts still have {role.Name} — remove it from them first.");

        await _editor.DeleteAsync(id, ct);
        return NoContent();
    }

    private AccountActor Caller => new(
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
        User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList(),
        User.FindAll(Permissions.ClaimType).Select(c => c.Value).ToList());

    private RoleDto ToDto(RoleRecord role) => new(
        role.Id, role.Name, role.Description, role.IsSystem, role.Permissions, role.AccountCount,
        CanEdit: RoleManagementPolicy.CanEdit(Caller, Snapshot(role)).Allowed);

    private static RoleSnapshot Snapshot(RoleRecord role) => new(role.Name, role.IsSystem, role.Permissions);

    /// <summary>
    /// What the caller could do once <paramref name="role"/> allows <paramref name="newPermissions"/>
    /// instead. Uses the caller's roles as stored now, not as their token lists them.
    /// </summary>
    private async Task<IReadOnlyCollection<string>> CallerPermissionsAfterAsync(RoleRecord role, IReadOnlyCollection<string> newPermissions, CancellationToken ct)
    {
        var callerRoles = (await _directory.GetAsync(Caller.UserId, ct))?.Roles ?? Caller.Roles.ToList();
        var catalog = (await _catalog.GetRolesAsync(ct))
            .Select(r => new RoleGrant(r.Name, r.Id == role.Id ? newPermissions.ToList() : r.Permissions))
            .ToList();
        return AccountManagementPolicy.PermissionsOf(callerRoles, catalog);
    }

    private static string? Validate(SaveRoleRequest request, out string name, out string? description, out IReadOnlyCollection<string> permissions)
    {
        name = request.Name?.Trim() ?? string.Empty;
        description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        permissions = (request.Permissions ?? []).Distinct().ToList();

        if (name.Length == 0) return "Enter a role name.";
        if (name.Length > NameMaxLength) return $"A role name must be {NameMaxLength} characters or fewer.";
        if (description?.Length > ApplicationRole.DescriptionMaxLength)
            return $"A description must be {ApplicationRole.DescriptionMaxLength} characters or fewer.";
        if (permissions.Any(string.IsNullOrWhiteSpace)) return "A permission can't be blank.";
        return permissions.FirstOrDefault(key => !Permissions.AllKeys.Contains(key)) is { } unknown
            ? $"{unknown} is not a permission."
            : null;
    }

    private BadRequestObjectResult RoleProblem(string detail) =>
        BadRequest(new ProblemDetails { Title = "Role not saved", Detail = detail, Status = StatusCodes.Status400BadRequest });

    private ObjectResult Refused(PolicyDecision decision) =>
        StatusCode(StatusCodes.Status403Forbidden,
            new ProblemDetails { Title = "Not allowed", Detail = decision.Reason, Status = StatusCodes.Status403Forbidden });
}

public record RoleDto(string Id, string Name, string? Description, bool IsSystem, IReadOnlyList<string> Permissions, int AccountCount, bool CanEdit);

public record PermissionDto(string Key, string Group, string Label, string Description);

public record SaveRoleRequest(string? Name, string? Description, IReadOnlyList<string>? Permissions);
