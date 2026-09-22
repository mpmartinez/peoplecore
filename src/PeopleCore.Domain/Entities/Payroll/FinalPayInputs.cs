namespace PeopleCore.Domain.Entities.Payroll;

/// <summary>
/// Per-run inputs for a <see cref="PayrollRunType.FinalPay"/> run: the working-day count Base
/// pay was computed from (kept for recompute so it does not drift off a later shift-schedule
/// change), which separation this run pays out, any HR override of the computed separation or
/// retirement pay, and the HR-added deductions taken after loans.
/// <para>
/// One row per <see cref="PayrollRun"/> - a FinalPay run's counterpart to a regular run's
/// PremiumDays/loan-deduction lines.
/// </para>
/// </summary>
public class FinalPayInputs : AuditableEntity
{
    public Guid PayrollRunId { get; set; }
    public PayrollRun PayrollRun { get; set; } = null!;

    /// <summary>The separation this final pay is for.</summary>
    public Guid SeparationId { get; set; }

    /// <summary>Days the employee's shift schedules in the final period; Base pay = DailyRate × WorkingDays.</summary>
    public decimal WorkingDays { get; set; }

    /// <summary>Replaces the computed separation pay, or adds one where none was computed. Requires <see cref="OverrideNote"/>.</summary>
    public decimal? SeparationPayOverride { get; set; }

    /// <summary>Replaces the computed retirement pay, or adds one where none was computed. Requires <see cref="OverrideNote"/>.</summary>
    public decimal? RetirementPayOverride { get; set; }

    /// <summary>Required whenever either override above is set.</summary>
    public string? OverrideNote { get; set; }

    public List<FinalPayDeduction> Deductions { get; set; } = [];
}

/// <summary>An HR-added deduction on a final-pay run, taken after loan balances.</summary>
public class FinalPayDeduction : AuditableEntity
{
    public Guid FinalPayInputsId { get; set; }
    public FinalPayInputs FinalPayInputs { get; set; } = null!;
    public string Label { get; set; } = "";
    public decimal Amount { get; set; }
}
