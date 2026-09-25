using FluentAssertions;
using PeopleCore.Domain.Entities.Employees;

namespace PeopleCore.Application.Tests.Leave;

/// <summary>
/// A solo parent ID grants Solo Parent Leave and the maternity extra 15 days only while it is on
/// file and unexpired. Both the number and the expiry must be present.
/// </summary>
public class SoloParentIdTests
{
    private static Employee AnEmployee(string? idNumber, DateOnly? validUntil) => new()
    {
        EmployeeNumber = "E0001",
        FirstName = "Juana",
        LastName = "Dela Cruz",
        WorkEmail = "juana@example.com",
        HireDate = new DateOnly(2020, 1, 1),
        SoloParentIdNumber = idNumber,
        SoloParentIdValidUntil = validUntil,
    };

    [Fact]
    public void ValidOnItsExpiryDate_IsTrue()
    {
        var employee = AnEmployee("SP-001", new DateOnly(2026, 9, 24));

        employee.HasValidSoloParentId(new DateOnly(2026, 9, 24)).Should().BeTrue();
    }

    [Fact]
    public void AfterExpiry_IsFalse()
    {
        var employee = AnEmployee("SP-001", new DateOnly(2026, 9, 24));

        employee.HasValidSoloParentId(new DateOnly(2026, 9, 25)).Should().BeFalse();
    }

    [Fact]
    public void MissingNumber_IsFalse()
    {
        var employee = AnEmployee(null, new DateOnly(2026, 9, 24));

        employee.HasValidSoloParentId(new DateOnly(2026, 9, 24)).Should().BeFalse();
    }

    [Fact]
    public void MissingDate_IsFalse()
    {
        var employee = AnEmployee("SP-001", null);

        employee.HasValidSoloParentId(new DateOnly(2026, 9, 24)).Should().BeFalse();
    }
}
