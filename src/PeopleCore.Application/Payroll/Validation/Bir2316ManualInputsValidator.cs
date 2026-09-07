using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Domain.Payroll;

namespace PeopleCore.Application.Payroll.Validation;

/// <summary>
/// Guards the overlay a caller supplies to <c>Bir2316Service.BuildAsync</c>.
/// <para>
/// The 2316 phase was careful that DERIVED figures can never come from the request body - that is
/// what stops a caller posting any withheld-tax figure they like and receiving a certificate
/// stating it. The fields a caller legitimately supplies were never bounded, so a negative or
/// absurd figure still reached the stamped form. This closes that half.
/// </para>
/// <para>
/// The empty overlay must pass: <c>GetPreviewAsync</c> builds a preview by calling
/// <c>BuildAsync</c> with <c>new Bir2316ManualInputs()</c>, so rejecting an all-default instance
/// would break preview for every employee.
/// </para>
/// </summary>
public static class Bir2316ManualInputsValidator
{
    public static void Validate(Bir2316ManualInputs manual)
    {
        var failures = new ValidationFailures();
        const decimal Max = PayrollInputLimits.MaxAnnualAmount;

        failures.AddMoney("Previous employer taxable compensation (Item 22)",
            manual.Item22_PrevTaxableCompensation, Max);
        failures.AddMoney("Previous employer tax withheld (Item 25B)",
            manual.Item25B_PrevTaxWithheld, Max);
        failures.AddMoney("PERA tax credit (Item 27)", manual.Item27_PeraTaxCredit, Max);
        failures.AddMoney("Hazard pay (Item 33)", manual.Item33_HazardPayMwe, Max);
        failures.AddMoney("De minimis benefits (Item 35)", manual.Item35_DeMinimis, Max);
        failures.AddMoney("Statutory minimum wage per day", manual.StatutoryMinWagePerDay, Max);
        failures.AddMoney("Statutory minimum wage per month", manual.StatutoryMinWagePerMonth, Max);

        // No relationship is asserted between the two minimum-wage figures. IsMinimumWageEarner is
        // deliberately not exposed, so neither reaches a rendered certificate, and inventing a
        // day-to-month rule for fields nothing consumes would be guessing.

        failures.AddIf(manual.Item25B_PrevTaxWithheld > manual.Item22_PrevTaxableCompensation,
            "Tax withheld by the previous employer (Item 25B) cannot exceed the taxable "
            + "compensation it was withheld from (Item 22).");

        failures.AddIf(manual.PrevEmployerName?.Length > PayrollInputLimits.MaxEmployerNameLength,
            $"Previous employer name cannot exceed {PayrollInputLimits.MaxEmployerNameLength} characters.");

        failures.AddIf(manual.PrevEmployerAddress?.Length > PayrollInputLimits.MaxEmployerAddressLength,
            $"Previous employer address cannot exceed {PayrollInputLimits.MaxEmployerAddressLength} characters.");

        if (!string.IsNullOrWhiteSpace(manual.PrevEmployerTin))
        {
            // Philippine TINs are 9 digits, or 12 with a branch code. Separators are accepted
            // because that is how people write them, and Bir2316Stamper draws the digits into
            // fixed cells regardless of how they were typed.
            var tin = manual.PrevEmployerTin;
            var digitCount = tin.Count(char.IsDigit);
            var onlyDigitsAndSeparators = tin.All(c => char.IsDigit(c) || c is '-' or ' ');

            failures.AddIf(!onlyDigitsAndSeparators || (digitCount != 9 && digitCount != 12),
                "Previous employer TIN must be 9 digits, or 12 with a branch code.");
        }

        if (!string.IsNullOrWhiteSpace(manual.PrevEmployerZipCode))
        {
            var zip = manual.PrevEmployerZipCode;

            failures.AddIf(zip.Length != 4 || !zip.All(char.IsDigit),
                "Previous employer ZIP code must be exactly 4 digits.");
        }

        failures.ThrowIfAny();
    }
}
