using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.Payroll.Services;
using PeopleCore.Domain.Entities.Attendance;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Entities.Scheduling;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Interfaces;
using PeopleCore.Domain.Payroll;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

/// <summary>
/// A run created from what the real attendance bridge derives pays an unworked double regular
/// holiday its second 100%, end to end.
/// </summary>
public partial class PayrollRunServiceTests
{
    [Fact]
    public async Task CreateAsync_FromTheRealBridge_PaysAnUnworkedDoubleRegularHoliday200Percent()
    {
        // 36,500 a month is 1,200 a day under the 365 factor.
        var employeeId = Guid.NewGuid();
        var holiday = new DateOnly(2026, 1, 5);   // a Monday, a scheduled working day
        var savedRun = SetupRoundTripRepositories(new EmployeeCompensation
        {
            EmployeeId = employeeId, BasicSalary = 36_500m, PayFrequency = PayFrequency.SemiMonthly, TaxCode = "S"
        });

        var template = new ShiftTemplate { Name = "Day", StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(17, 0) };
        var attendance = new Mock<IAttendanceRepository>();
        attendance.Setup(r => r.GetAllByPeriodAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync([]);
        var leave = new Mock<ILeaveRequestRepository>();
        leave.Setup(r => r.GetApprovedByPeriodAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync([]);
        var overtime = new Mock<IOvertimeRepository>();
        overtime.Setup(r => r.GetApprovedByPeriodAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
        var holidays = new Mock<IHolidayRepository>();
        holidays.Setup(r => r.GetByYearAsync(2026, It.IsAny<CancellationToken>()))
                .ReturnsAsync([
                    new Holiday { Name = "First", HolidayDate = holiday, HolidayType = HolidayType.RegularHoliday },
                    new Holiday { Name = "Second", HolidayDate = holiday, HolidayType = HolidayType.RegularHoliday }
                ]);
        var assignments = new Mock<IShiftAssignmentRepository>();
        assignments.Setup(r => r.GetActiveForPeriodAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(),
                                                         It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync([new EmployeeShiftAssignment
                   {
                       EmployeeId = employeeId, ShiftTemplateId = template.Id, ShiftTemplate = template,
                       EffectiveFrom = new DateOnly(2026, 1, 1)
                   }]);

        var sut = new PayrollRunService(
            _runRepo.Object, _compensationRepo.Object, _allowanceRepo.Object, _loanRepo.Object, _settingsRepo.Object,
            new PayrollComputationService(),
            new PayrollAttendanceBridge(attendance.Object, leave.Object, overtime.Object, holidays.Object, assignments.Object),
            _employeeRepo.Object, _separations.Object, NullLogger<PayrollRunService>.Instance,
            _finalPay.Object, _yearEnd.Object, _clock);

        await sut.CreateAsync(RoundTripRequest(employeeId), CancellationToken.None);

        // The unworked double holiday adds 1,200 x (2.00 - 1.00); the day itself carries no absence.
        var entry = savedRun()!.Employees.Single();
        entry.HolidayPay.Should().Be(1_200.00m);
        entry.PremiumDays.Should().ContainSingle(d =>
            d.DayType == WorkDayType.DoubleRegularHoliday && d.UnworkedDays == 1m);
    }
}
