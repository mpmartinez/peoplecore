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
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

/// <summary>
/// A run created from what the real attendance bridge derives deducts an unworked regular
/// holiday that follows an unpaid absence (Labor Code Art. 94), end to end.
/// </summary>
public partial class PayrollRunServiceTests
{
    [Fact]
    public async Task CreateAsync_FromTheRealBridge_DeductsAnUnworkedRegularHolidayAfterAnUnpaidAbsenceTheDayBefore()
    {
        // 36,500 a month is 1,200 a day under the 365 factor. The period is Jan 1-15 2026.
        var employeeId = Guid.NewGuid();
        // Fri Jan 2 is not attended: the qualifying day for the holiday.
        var holiday = new DateOnly(2026, 1, 5);        // a Monday, a regular holiday, not worked
        var savedRun = SetupRoundTripRepositories(new EmployeeCompensation
        {
            EmployeeId = employeeId, BasicSalary = 36_500m, PayFrequency = PayFrequency.SemiMonthly, TaxCode = "S"
        });

        // Present on every other weekday of the period: Thu Jan 1, Tue Jan 6 to Fri Jan 9, and
        // Mon Jan 12 to Thu Jan 15. Thu Jan 1 is an ordinary present day here, because the
        // holiday mock holds only Jan 5.
        var presentDays = new[] { 1, 6, 7, 8, 9, 12, 13, 14, 15 }.Select(d => new DateOnly(2026, 1, d));
        var records = presentDays
            .Select(d => new AttendanceRecord { EmployeeId = employeeId, AttendanceDate = d, IsPresent = true })
            .ToList();

        var template = new ShiftTemplate { Name = "Day", StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(17, 0) };
        var attendance = new Mock<IAttendanceRepository>();
        attendance.Setup(r => r.GetAllByPeriodAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync((DateOnly from, DateOnly to, CancellationToken _) =>
                      records.Where(r => r.AttendanceDate >= from && r.AttendanceDate <= to).ToList());
        var leave = new Mock<ILeaveRequestRepository>();
        leave.Setup(r => r.GetApprovedByPeriodAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync([]);
        var overtime = new Mock<IOvertimeRepository>();
        overtime.Setup(r => r.GetApprovedByPeriodAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
        var holidays = new Mock<IHolidayRepository>();
        holidays.Setup(r => r.GetByYearAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        holidays.Setup(r => r.GetByYearAsync(2026, It.IsAny<CancellationToken>()))
                .ReturnsAsync([new Holiday { Name = "Regular holiday", HolidayDate = holiday, HolidayType = HolidayType.RegularHoliday }]);
        var assignments = new Mock<IShiftAssignmentRepository>();
        assignments.Setup(r => r.GetActiveForPeriodAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(),
                                                         It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync([new EmployeeShiftAssignment
                   {
                       EmployeeId = employeeId, ShiftTemplateId = template.Id, ShiftTemplate = template,
                       EffectiveFrom = new DateOnly(2025, 12, 1)
                   }]);

        var sut = new PayrollRunService(
            _runRepo.Object, _compensationRepo.Object, _allowanceRepo.Object, _loanRepo.Object, _settingsRepo.Object,
            new PayrollComputationService(),
            new PayrollAttendanceBridge(attendance.Object, leave.Object, overtime.Object, holidays.Object, assignments.Object),
            _employeeRepo.Object, _separations.Object, NullLogger<PayrollRunService>.Instance,
            _finalPay.Object, _yearEnd.Object, _clock);

        await sut.CreateAsync(RoundTripRequest(employeeId), CancellationToken.None);

        // The Friday is an absence, and the Monday holiday after it (Sat 3 and Sun 4 are rest
        // days) is not paid, so it is deducted at the daily rate too.
        var entry = savedRun()!.Employees.Single();
        entry.AbsenceDays.Should().Be(2m);
        entry.AbsenceDeduction.Should().Be(2_400.00m);
        entry.HolidayPay.Should().Be(0m);
        entry.PremiumDays.Should().BeEmpty();
    }
}
