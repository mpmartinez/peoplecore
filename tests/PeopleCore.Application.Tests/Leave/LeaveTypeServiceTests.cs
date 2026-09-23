using System.Text.Json;
using FluentAssertions;
using Moq;
using PeopleCore.Application.Leave.DTOs;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.Leave.Services;
using PeopleCore.Domain.Entities.Leave;
using Xunit;

namespace PeopleCore.Application.Tests.Leave;

/// <summary>
/// A leave type says whether its unused days are paid out on final pay, and whether those days
/// count toward the 10-day de minimis ceiling on converted vacation leave.
/// </summary>
public class LeaveTypeServiceTests
{
    private readonly Mock<ILeaveTypeRepository> _repo = new();
    private readonly LeaveTypeService _sut;

    public LeaveTypeServiceTests()
    {
        _repo.Setup(r => r.AddAsync(It.IsAny<LeaveType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LeaveType lt, CancellationToken _) => lt);
        _sut = new LeaveTypeService(_repo.Object);
    }

    private static CreateLeaveTypeDto Request(bool convertible, bool countsAsVacation) => new(
        "Vacation Leave", "VL", 15m, true, false, null, null, false, convertible, countsAsVacation);

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task Create_StoresAndReturnsTheCashSettings(bool convertible, bool countsAsVacation)
    {
        LeaveType? saved = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<LeaveType>(), It.IsAny<CancellationToken>()))
            .Callback((LeaveType lt, CancellationToken _) => saved = lt)
            .ReturnsAsync((LeaveType lt, CancellationToken _) => lt);

        var dto = await _sut.CreateAsync(Request(convertible, countsAsVacation));

        saved!.IsConvertibleToCash.Should().Be(convertible);
        saved.CountsAsVacationForDeMinimis.Should().Be(countsAsVacation);
        dto.IsConvertibleToCash.Should().Be(convertible);
        dto.CountsAsVacationForDeMinimis.Should().Be(countsAsVacation);
    }

    [Fact]
    public async Task Update_ChangesTheCashSettings()
    {
        var existing = new LeaveType { Name = "Sick Leave", Code = "SL", MaxDaysPerYear = 15m };
        _repo.Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var dto = await _sut.UpdateAsync(existing.Id, Request(convertible: true, countsAsVacation: false));

        existing.IsConvertibleToCash.Should().BeTrue();
        existing.CountsAsVacationForDeMinimis.Should().BeFalse();
        dto.IsConvertibleToCash.Should().BeTrue();
        dto.CountsAsVacationForDeMinimis.Should().BeFalse();
        _repo.Verify(r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetById_ReadsTheCashSettings()
    {
        var existing = new LeaveType
        {
            Name = "Vacation Leave", Code = "VL", MaxDaysPerYear = 15m,
            IsConvertibleToCash = true, CountsAsVacationForDeMinimis = true,
        };
        _repo.Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var dto = await _sut.GetByIdAsync(existing.Id);

        dto.IsConvertibleToCash.Should().BeTrue();
        dto.CountsAsVacationForDeMinimis.Should().BeTrue();
    }

    [Fact]
    public void ARequestWithoutTheCashSettings_TakesTheEntitysDefaults()
    {
        // A client written before the settings existed still posts a valid body: not convertible,
        // and counting as vacation should it later be made convertible.
        const string json = """
            {"name":"Sick Leave","code":"SL","maxDaysPerYear":15,"isPaid":true,"isCarryOver":false,
             "carryOverMaxDays":null,"genderRestriction":null,"requiresDocument":false}
            """;

        var dto = JsonSerializer.Deserialize<CreateLeaveTypeDto>(json, JsonSerializerOptions.Web)!;

        dto.IsConvertibleToCash.Should().Be(new LeaveType().IsConvertibleToCash).And.BeFalse();
        dto.CountsAsVacationForDeMinimis.Should().Be(new LeaveType().CountsAsVacationForDeMinimis).And.BeTrue();
    }
}
