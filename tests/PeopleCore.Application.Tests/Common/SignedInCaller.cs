using Moq;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Employees.Services;
using PeopleCore.Infrastructure.Identity;

namespace PeopleCore.Application.Tests.Common;

/// <summary>
/// The caller a controller authorization test signs in as, wired to the real
/// <see cref="EmployeeAccessService"/> so the tests exercise the actual role rules. The org chart is
/// one line: <see cref="DirectReport"/> reports to <see cref="Caller"/>; <see cref="Stranger"/>
/// reports to nobody the caller knows.
/// </summary>
internal sealed class SignedInCaller
{
    public static readonly Guid Caller = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Stranger = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid DirectReport = Guid.Parse("55555555-5555-5555-5555-555555555555");

    public static TheoryData<string> HrRoles => new() { "Admin", "HRManager" };
    public static TheoryData<string> DeciderRoles => new() { "Admin", "HRManager", "Manager" };

    public Mock<ICurrentUserService> CurrentUser { get; } = new();
    public Mock<IEmployeeRepository> Employees { get; } = new();
    public IEmployeeAccessService Access { get; }

    public SignedInCaller()
    {
        // Default: a rank-and-file employee holding no privileged role.
        As(Caller);
        Employees.Setup(r => r.IsDirectReportAsync(DirectReport, Caller, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        Access = new EmployeeAccessService(CurrentUser.Object, Employees.Object);
    }

    public void As(Guid? employeeId, params string[] roles)
    {
        // Phrased in roles, granted as the seeded roles' permissions: every authorization test built
        // on this therefore also proves the seeded roles keep the reach the old role checks gave.
        var granted = SeededRoles.PermissionsOf(roles);
        CurrentUser.Setup(c => c.EmployeeId).Returns(employeeId);
        CurrentUser.Setup(c => c.HasPermission(It.IsAny<string>())).Returns((string key) => granted.Contains(key));
    }

    /// <summary>Signs in holding exactly <paramref name="permissions"/> - for a mix no seeded role has.</summary>
    public void Holding(Guid? employeeId, params string[] permissions)
    {
        CurrentUser.Setup(c => c.EmployeeId).Returns(employeeId);
        CurrentUser.Setup(c => c.HasPermission(It.IsAny<string>())).Returns((string key) => permissions.Contains(key));
    }
}
