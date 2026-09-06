using FluentAssertions;
using Moq;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Application.Payroll.Services;
using PeopleCore.Domain.Entities.Payroll;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

public class PayrollSettingsServiceTests
{
    private readonly Mock<IPayrollSettingsRepository> _repo = new();
    private readonly PayrollSettingsService _sut;

    public PayrollSettingsServiceTests()
    {
        _sut = new PayrollSettingsService(_repo.Object);
    }

    private static PayrollSettingsDto MakeDto(Guid companyId) => new(
        CompanyId: companyId,
        PhilHealthRate: 0.05m,
        PhilHealthMinShare: 250m,
        PhilHealthMaxShare: 2_500m,
        PagIbigEmployeeRate: 0.02m,
        PagIbigLowEmployeeRate: 0.01m,
        PagIbigLowRateThreshold: 1_500m,
        PagIbigEmployerRate: 0.02m,
        PagIbigMaxFundSalary: 10_000m,
        DailyRateFactor: 365m,
        SSSEmployeeRate: null,
        SSSEmployerRate: null);

    [Fact]
    public async Task UpdateAsync_WhenARowExistsForAnotherCompany_ThrowsInvalidOperationException()
    {
        // PayrollRun carries no CompanyId (see IPayrollSettingsRepository.GetDefaultAsync), so a
        // second row would leave payroll computation unable to tell which company's rates apply
        // to any given run - and GetDefaultAsync throws for every run and compute thereafter.
        var otherCompanyId = Guid.NewGuid();
        var thisCompanyId = Guid.NewGuid();
        var existingRow = new PayrollSettings { CompanyId = otherCompanyId };

        _repo.Setup(r => r.GetByCompanyIdAsync(thisCompanyId, It.IsAny<CancellationToken>()))
             .ReturnsAsync((PayrollSettings?)null);
        _repo.Setup(r => r.GetDefaultAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(existingRow);

        var act = () => _sut.UpdateAsync(thisCompanyId, MakeDto(thisCompanyId), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _repo.Verify(r => r.AddAsync(It.IsAny<PayrollSettings>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_WhenUpdatingTheExistingCompanysOwnRow_Succeeds()
    {
        var companyId = Guid.NewGuid();
        var existingRow = new PayrollSettings { CompanyId = companyId };

        _repo.Setup(r => r.GetByCompanyIdAsync(companyId, It.IsAny<CancellationToken>()))
             .ReturnsAsync(existingRow);

        var dto = MakeDto(companyId) with { PhilHealthRate = 0.06m };

        await _sut.UpdateAsync(companyId, dto, CancellationToken.None);

        existingRow.PhilHealthRate.Should().Be(0.06m);
        _repo.Verify(r => r.UpdateAsync(existingRow, It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.AddAsync(It.IsAny<PayrollSettings>(), It.IsAny<CancellationToken>()), Times.Never);
        // Updating an existing row must not need to consult which other company owns a row.
        _repo.Verify(r => r.GetDefaultAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_WhenNoSettingsRowExistsAtAll_InsertsTheFirstRow()
    {
        var companyId = Guid.NewGuid();

        _repo.Setup(r => r.GetByCompanyIdAsync(companyId, It.IsAny<CancellationToken>()))
             .ReturnsAsync((PayrollSettings?)null);
        _repo.Setup(r => r.GetDefaultAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync((PayrollSettings?)null);
        _repo.Setup(r => r.AddAsync(It.IsAny<PayrollSettings>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((PayrollSettings s, CancellationToken _) => s);

        await _sut.UpdateAsync(companyId, MakeDto(companyId), CancellationToken.None);

        _repo.Verify(r => r.AddAsync(
            It.Is<PayrollSettings>(s => s.CompanyId == companyId), It.IsAny<CancellationToken>()), Times.Once);
    }
}
