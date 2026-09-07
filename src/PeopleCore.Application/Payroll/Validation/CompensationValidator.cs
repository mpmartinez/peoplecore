using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Domain.Payroll;

namespace PeopleCore.Application.Payroll.Validation;

/// <summary>
/// Guards <c>EmployeeCompensationService.UpsertAsync</c>. Its request is copied straight onto the
/// entity and saved, so anything not rejected here is persisted, computed into a payslip, and
/// aggregated into a BIR Form 2316 that an officer of the company signs.
/// </summary>
public static class CompensationValidator
{
    public static void Validate(UpsertCompensationRequest request)
    {
        var failures = new ValidationFailures();

        failures.AddMoney("Basic salary", request.BasicSalary, PayrollInputLimits.MaxMonthlyBasicSalary);

        failures.AddIf(!Enum.IsDefined(request.PayFrequency),
            "Pay frequency is not a recognised value.");

        failures.AddIf(request.Dependents < 0, "Dependents cannot be negative.");
        failures.AddIf(request.Dependents > PayrollInputLimits.MaxDependents,
            $"Dependents cannot exceed {PayrollInputLimits.MaxDependents}.");

        // Length only, never a status-code whitelist: nothing reads TaxCode, and TRAIN abolished
        // the exemptions those codes encoded. See CompensationValidatorTests for the full reasoning.
        failures.AddIf(string.IsNullOrWhiteSpace(request.TaxCode), "Tax code is required.");
        failures.AddIf(request.TaxCode?.Length > PayrollInputLimits.MaxTaxCodeLength,
            $"Tax code cannot exceed {PayrollInputLimits.MaxTaxCodeLength} characters.");

        failures.ThrowIfAny();
    }
}
