using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Enums;

namespace PeopleCore.Domain.Entities.Payroll;

/// <summary>
/// The SSS maternity benefit for one approved maternity leave request (RA 11210). The employer
/// advances it once, tax-free, on a payroll HR picks, then claims it back from SSS.
/// One claim per request.
/// </summary>
public class MaternityClaim : AuditableEntity
{
    public Guid LeaveRequestId { get; set; }
    public LeaveRequest LeaveRequest { get; set; } = null!;

    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;

    /// <summary>The SSS daily maternity allowance. Null until HR sets it; the claim cannot be advanced without it.</summary>
    public decimal? DailyAllowance { get; set; }

    /// <summary>The leave days the benefit covers: the request's TotalDays when the claim was created.</summary>
    public decimal Days { get; set; }

    /// <summary><see cref="DailyAllowance"/> x <see cref="Days"/>, rounded to 2 dp. Zero until the allowance is set.</summary>
    public decimal Benefit { get; set; }

    public MaternityClaimStatus Status { get; set; } = MaternityClaimStatus.Draft;

    /// <summary>The payroll run that advanced the benefit. Set when that run is marked paid; cleared if the run is deleted.</summary>
    public Guid? AdvanceRunId { get; set; }
    public PayrollRun? AdvanceRun { get; set; }

    /// <summary>The pay date of the run that advanced the benefit.</summary>
    public DateOnly? AdvancedAt { get; set; }

    public DateOnly? ReimbursedOn { get; set; }
    public decimal? ReimbursedAmount { get; set; }

    /// <summary>Why the reimbursement differs from the benefit, or why SSS denied the claim.</summary>
    public string? Note { get; set; }
}
