using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Payroll;

namespace PeopleCore.Application.Payroll.Validation;

/// <summary>
/// Guards <c>PayrollRunService.CreateAsync</c>. A run's dates decide which tax year its income is
/// attributed to on Form 2316, so an incoherent period is not merely untidy - it moves money
/// between tax years.
/// </summary>
public static class PayrollRunRequestValidator
{
    public static void Validate(CreatePayrollRunRequest request)
    {
        var failures = new ValidationFailures();

        var hasEmployees = request.Employees is { Count: > 0 };
        failures.AddIf(!hasEmployees, "A payroll run must include at least one employee.");

        var frequencyIsKnown = Enum.IsDefined(request.Frequency);
        failures.AddIf(!frequencyIsKnown, "Pay frequency is not a recognised value.");

        var periodIsOrdered = request.PeriodEnd >= request.PeriodStart;
        failures.AddIf(!periodIsOrdered, "Period end cannot be before period start.");

        failures.AddIf(request.PayDate < request.PeriodStart,
            "Pay date cannot be before period start.");

        // Only meaningful once the period is ordered and the frequency is known - measuring the
        // length of an inverted period would report a second, confusing failure for one mistake.
        if (periodIsOrdered && frequencyIsKnown)
        {
            var days = request.PeriodEnd.DayNumber - request.PeriodStart.DayNumber + 1;
            var maxDays = request.Frequency == PayFrequency.SemiMonthly
                ? PayrollInputLimits.MaxSemiMonthlyPeriodDays
                : PayrollInputLimits.MaxMonthlyPeriodDays;

            failures.AddIf(days > maxDays,
                $"A {request.Frequency} period cannot span more than {maxDays} days; this one spans {days}.");
        }

        if (hasEmployees)
        {
            var duplicates = request.Employees
                .GroupBy(e => e.EmployeeId)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            failures.AddIf(duplicates.Count > 0,
                $"An employee can appear only once in a run; duplicated: {string.Join(", ", duplicates)}.");

            foreach (var employee in request.Employees)
            {
                // Each override is checked ONLY when supplied. Null means "use what the attendance
                // bridge derived from punches, approved leave, approved overtime, the holiday
                // calendar and the shift schedule" - see PayrollRunEmployeeInput's own remarks.
                AddBoundsCheck(failures, employee.DaysWorked, 0m, PayrollInputLimits.MaxDaysInPeriod,
                    $"Days worked for employee {employee.EmployeeId}");
                AddBoundsCheck(failures, employee.OvertimeHours, 0m, PayrollInputLimits.MaxOvertimeHoursInPeriod,
                    $"Overtime hours for employee {employee.EmployeeId}");
                AddBoundsCheck(failures, employee.HolidayDays, 0m, PayrollInputLimits.MaxDaysInPeriod,
                    $"Holiday days for employee {employee.EmployeeId}");
            }
        }

        failures.ThrowIfAny();
    }

    private static void AddBoundsCheck(
        ValidationFailures failures, decimal? value, decimal min, decimal max, string fieldName)
    {
        if (value is not decimal supplied)
            return;

        failures.AddIf(supplied < min || supplied > max,
            $"{fieldName} must be between {min:N0} and {max:N0}.");
    }
}
