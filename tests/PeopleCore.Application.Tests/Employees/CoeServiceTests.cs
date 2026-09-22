using FluentAssertions;
using M2NET.Core.Enums;
using Moq;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Employees.Coe;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Organization.Interfaces;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Organization;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using Xunit;

namespace PeopleCore.Application.Tests.Employees;

/// <summary>
/// The facts CoeService hands off to CoeContent.Build/ICoeRenderer, and the file name it produces.
/// The clock is fixed at 2026-09-22 in Manila throughout - the file name embeds it.
/// </summary>
public class CoeServiceTests
{
    private static readonly Guid EmployeeId = Guid.NewGuid();

    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<ICompanyRepository> _companies = new();
    private readonly Mock<IEmployeeCompensationRepository> _compensations = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly Mock<ICoeRenderer> _renderer = new();
    private readonly CoeService _sut;

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    public CoeServiceTests()
    {
        _companies.Setup(c => c.GetDefaultAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Company());
        _compensations.Setup(c => c.GetByEmployeeIdAsync(EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeCompensation { EmployeeId = EmployeeId, BasicSalary = 35_000m });
        _currentUser.Setup(c => c.Email).Returns("hr@company.test");

        _renderer.Setup(r => r.Render(It.IsAny<CoeContent>())).Returns("%PDF-fake"u8.ToArray());

        _sut = new CoeService(_employees.Object, _companies.Object, _compensations.Object, _currentUser.Object,
            _renderer.Object, new FixedClock(new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public async Task GenerateAsync_WhenTheEmployeeDoesNotExist_ThrowsKeyNotFound()
    {
        var act = () => _sut.GenerateAsync(EmployeeId, new CoeRequest(null, null, null, false), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task GenerateAsync_ForACurrentEmployee_PassesANullLastWorkingDay()
    {
        _employees.Setup(e => e.GetByIdAsync(EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Employee(separationDate: null));
        CoeContent? captured = null;
        _renderer.Setup(r => r.Render(It.IsAny<CoeContent>()))
            .Callback((CoeContent c) => captured = c)
            .Returns("%PDF-fake"u8.ToArray());

        await _sut.GenerateAsync(EmployeeId, new CoeRequest(null, null, null, false), CancellationToken.None);

        captured!.Paragraphs[0].Should().Contain("to present");
    }

    [Fact]
    public async Task GenerateAsync_ForAFormerEmployee_PassesTheirSeparationDateAsTheLastWorkingDay()
    {
        var separationDate = new DateOnly(2026, 8, 31);
        _employees.Setup(e => e.GetByIdAsync(EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Employee(separationDate: separationDate));
        CoeContent? captured = null;
        _renderer.Setup(r => r.Render(It.IsAny<CoeContent>()))
            .Callback((CoeContent c) => captured = c)
            .Returns("%PDF-fake"u8.ToArray());

        await _sut.GenerateAsync(EmployeeId, new CoeRequest(null, null, null, false), CancellationToken.None);

        captured!.Paragraphs[0].Should().Contain("to August 31, 2026");
    }

    [Fact]
    public async Task GenerateAsync_PassesThePositionTitle()
    {
        _employees.Setup(e => e.GetByIdAsync(EmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(Employee());
        CoeContent? captured = null;
        _renderer.Setup(r => r.Render(It.IsAny<CoeContent>()))
            .Callback((CoeContent c) => captured = c)
            .Returns("%PDF-fake"u8.ToArray());

        await _sut.GenerateAsync(EmployeeId, new CoeRequest(null, null, null, false), CancellationToken.None);

        captured!.Paragraphs[0].Should().Contain("as Payroll Officer");
    }

    [Fact]
    public async Task GenerateAsync_WithSalaryRequested_PassesTheCompensationRecordsBasicSalary()
    {
        _employees.Setup(e => e.GetByIdAsync(EmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(Employee());
        CoeContent? captured = null;
        _renderer.Setup(r => r.Render(It.IsAny<CoeContent>()))
            .Callback((CoeContent c) => captured = c)
            .Returns("%PDF-fake"u8.ToArray());

        await _sut.GenerateAsync(EmployeeId, new CoeRequest(null, null, null, true), CancellationToken.None);

        captured!.Paragraphs.Should().Contain(p => p.Contains("₱35,000.00"));
    }

    [Fact]
    public async Task GenerateAsync_WithNoSignatoryGiven_UsesTheCurrentUsersEmail()
    {
        _employees.Setup(e => e.GetByIdAsync(EmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(Employee());
        CoeContent? captured = null;
        _renderer.Setup(r => r.Render(It.IsAny<CoeContent>()))
            .Callback((CoeContent c) => captured = c)
            .Returns("%PDF-fake"u8.ToArray());

        await _sut.GenerateAsync(EmployeeId, new CoeRequest(null, null, null, false), CancellationToken.None);

        captured!.SignatoryName.Should().Be("hr@company.test");
    }

    [Fact]
    public async Task GenerateAsync_ReturnsAFileNameBuiltFromTheEmployeesNameAndToday()
    {
        _employees.Setup(e => e.GetByIdAsync(EmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(Employee());

        var (_, fileName) = await _sut.GenerateAsync(EmployeeId, new CoeRequest(null, null, null, false), CancellationToken.None);

        fileName.Should().Be("COE-CruzJuan-20260922.pdf");
    }

    private static Employee Employee(DateOnly? separationDate = null) => new()
    {
        Id = EmployeeId,
        EmployeeNumber = "EMP-001",
        FirstName = "Juan",
        LastName = "Cruz",
        DateOfBirth = new DateOnly(1990, 1, 1),
        Gender = Gender.Male,
        WorkEmail = "juan@company.com",
        EmploymentStatus = EmploymentStatus.Regular,
        EmploymentType = EmploymentType.Regular,
        HireDate = new DateOnly(2021, 3, 1),
        SeparationDate = separationDate,
        IsActive = separationDate is null,
        Position = new Position { Title = "Payroll Officer" }
    };

    private static Company Company() => new()
    {
        Name = "Acme Inc.",
        Address = "123 Ayala Ave, Makati"
    };
}
