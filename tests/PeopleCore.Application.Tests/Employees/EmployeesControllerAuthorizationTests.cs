using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PeopleCore.API.Controllers.Employees;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Employees.DTOs;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Domain.Enums;
using Xunit;

namespace PeopleCore.Application.Tests.Employees;

/// <summary>
/// Every action on <see cref="EmployeesController"/> that takes an employee id from the route is
/// an IDOR waiting to happen: the id is caller-supplied, so nothing but an explicit ownership
/// check stops one authenticated employee reading or rewriting another's record. These tests pin
/// three things per route - a stranger is refused, the service is never reached when refused, and
/// the owner (and HR) still get through - so a future edit that drops a guard fails here rather
/// than in production.
/// </summary>
public class EmployeesControllerAuthorizationTests
{
    private static readonly Guid Caller = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Stranger = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DocumentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ContactId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly Mock<IEmployeeService> _service = new();
    private readonly Mock<IEmployeeDocumentService> _documents = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly EmployeesController _sut;

    public EmployeesControllerAuthorizationTests()
    {
        // Default caller: a rank-and-file employee holding no privileged role.
        _currentUser.Setup(c => c.EmployeeId).Returns(Caller);
        _currentUser.Setup(c => c.IsInRole(It.IsAny<string>())).Returns(false);
        _sut = new EmployeesController(_service.Object, _documents.Object, _currentUser.Object);
    }

    private void SignInAs(Guid? employeeId, params string[] roles)
    {
        _currentUser.Setup(c => c.EmployeeId).Returns(employeeId);
        _currentUser.Setup(c => c.IsInRole(It.IsAny<string>())).Returns((string r) => roles.Contains(r));
    }

    /// <summary>
    /// Each guarded action, invoked against <paramref name="id"/>. Keeping them in one place means
    /// a new employee-scoped action is one line away from being covered by every case below.
    /// </summary>
    private static readonly string[] GuardedActionNames =
    [
        nameof(EmployeesController.GetById),
        nameof(EmployeesController.GetGovernmentIds),
        nameof(EmployeesController.UpsertGovernmentId),
        nameof(EmployeesController.GetEmergencyContacts),
        nameof(EmployeesController.AddEmergencyContact),
        nameof(EmployeesController.DeleteEmergencyContact),
        nameof(EmployeesController.GetDocuments),
        nameof(EmployeesController.UploadDocument),
        nameof(EmployeesController.GetDownloadUrl)
    ];

    public static TheoryData<string> GuardedActions
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var name in GuardedActionNames)
                data.Add(name);
            return data;
        }
    }

    private Task<IActionResult> Invoke(string action, Guid id) => action switch
    {
        nameof(EmployeesController.GetById) =>
            _sut.GetById(id, CancellationToken.None),
        nameof(EmployeesController.GetGovernmentIds) =>
            _sut.GetGovernmentIds(id, CancellationToken.None),
        nameof(EmployeesController.UpsertGovernmentId) =>
            _sut.UpsertGovernmentId(id, new UpsertGovernmentIdDto(GovernmentIdType.SSS, "34-1234567-8"), CancellationToken.None),
        nameof(EmployeesController.GetEmergencyContacts) =>
            _sut.GetEmergencyContacts(id, CancellationToken.None),
        nameof(EmployeesController.AddEmergencyContact) =>
            _sut.AddEmergencyContact(id, NewContact(), CancellationToken.None),
        nameof(EmployeesController.DeleteEmergencyContact) =>
            _sut.DeleteEmergencyContact(id, ContactId, CancellationToken.None),
        nameof(EmployeesController.GetDocuments) =>
            _sut.GetDocuments(id, CancellationToken.None),
        nameof(EmployeesController.UploadDocument) =>
            _sut.UploadDocument(id, NewFile(), DocumentType.Contract, CancellationToken.None),
        nameof(EmployeesController.GetDownloadUrl) =>
            _sut.GetDownloadUrl(id, DocumentId, CancellationToken.None),
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unmapped controller action.")
    };

    [Theory]
    [MemberData(nameof(GuardedActions))]
    public async Task Action_WhenCallerIsNotTheEmployeeAndHoldsNoPrivilegedRole_ReturnsForbid(string action)
    {
        var result = await Invoke(action, Stranger);

        result.Should().BeOfType<ForbidResult>(
            $"{action} must not serve another employee's record to an unprivileged caller");
    }

    [Theory]
    [MemberData(nameof(GuardedActions))]
    public async Task Action_WhenCallerHasNoEmployeeIdClaim_ReturnsForbid(string action)
    {
        // A token with no employee_id claim identifies nobody. It must not fall through to the
        // route's id - that would make an unlinked account a master key.
        SignInAs(null);

        var result = await Invoke(action, Stranger);

        result.Should().BeOfType<ForbidResult>($"{action} must refuse a caller with no employee_id claim");
    }

    [Theory]
    [MemberData(nameof(GuardedActions))]
    public async Task Action_WhenRefused_NeverReachesTheService(string action)
    {
        await Invoke(action, Stranger);

        _service.VerifyNoOtherCalls();
        _documents.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(GuardedActions))]
    public async Task Action_WhenCallerIsTheEmployee_IsAllowedThrough(string action)
    {
        var result = await Invoke(action, Caller);

        result.Should().NotBeOfType<ForbidResult>($"{action} is self-service - the owner must get through");
    }

    [Theory]
    [MemberData(nameof(GuardedActions))]
    public async Task Action_WhenCallerIsHrManager_IsAllowedThroughForAnyEmployee(string action)
    {
        SignInAs(Caller, "HRManager");

        var result = await Invoke(action, Stranger);

        result.Should().NotBeOfType<ForbidResult>($"{action} must stay open to HR for any employee");
    }

    [Theory]
    [MemberData(nameof(GuardedActions))]
    public async Task Action_WhenCallerIsAdmin_IsAllowedThroughForAnyEmployee(string action)
    {
        SignInAs(Caller, "Admin");

        var result = await Invoke(action, Stranger);

        result.Should().NotBeOfType<ForbidResult>($"{action} must stay open to Admin for any employee");
    }

    [Fact]
    public async Task GetById_WhenCallerIsPayrollService_IsAllowedThroughForAnyEmployee()
    {
        // The compensation screen (Admin,HRManager,PayrollService) reads the employee record to
        // show whose compensation is on screen - narrowing GetById to HR alone would break it.
        SignInAs(Caller, "PayrollService");

        var result = await _sut.GetById(Stranger, CancellationToken.None);

        result.Should().NotBeOfType<ForbidResult>();
    }

    [Fact]
    public async Task GetGovernmentIds_WhenCallerIsPayrollService_ReturnsForbidForAnotherEmployee()
    {
        // PayrollService gets the employee record, not everyone's SSS/TIN numbers - payroll
        // computation reads those server-side, never through this endpoint.
        SignInAs(Caller, "PayrollService");

        var result = await _sut.GetGovernmentIds(Stranger, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public void UpsertGovernmentId_DoesNotRelyOnAnEmployeeRoleList()
    {
        // The route used to carry [Authorize(Roles = "Admin,HRManager,Employee")]. "Employee" in a
        // role list authorises nothing here: it is held by every user and says nothing about WHICH
        // record the caller may write. If it comes back, someone has mistaken it for a boundary.
        var attribute = typeof(EmployeesController)
            .GetMethod(nameof(EmployeesController.UpsertGovernmentId))!
            .GetCustomAttribute<AuthorizeAttribute>();

        attribute?.Roles?.Split(',', StringSplitOptions.TrimEntries)
            .Should().NotContain("Employee",
                "an \"Employee\" role grant is not an ownership check - the guard in the action body is");
    }

    [Fact]
    public void EveryEmployeeScopedAction_IsCoveredByTheseTests()
    {
        // A new action taking an employee id would otherwise ship unguarded and unnoticed. This
        // fails the moment one is added without being wired into GuardedActions above.
        var covered = GuardedActionNames
            .Concat([
                nameof(EmployeesController.Update),
                nameof(EmployeesController.Deactivate),
                nameof(EmployeesController.DeleteDocument)
            ])
            .ToHashSet();

        var employeeScoped = typeof(EmployeesController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetParameters().Any(p => p.ParameterType == typeof(Guid) && p.Name == "id"))
            .Select(m => m.Name);

        employeeScoped.Should().BeSubsetOf(covered,
            "every action taking an employee id must either be role-locked to HR or listed in GuardedActions");
    }

    private static CreateEmergencyContactDto NewContact() =>
        new("Maria dela Cruz", "Spouse", "+639171234567", null);

    private static IFormFile NewFile()
    {
        var bytes = new byte[] { 1, 2, 3 };
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "contract.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };
    }
}
