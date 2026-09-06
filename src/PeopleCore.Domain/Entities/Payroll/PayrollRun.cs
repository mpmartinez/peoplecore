using PeopleCore.Domain.Enums;

namespace PeopleCore.Domain.Entities.Payroll;

public class PayrollRun : AuditableEntity
{
    public string RunNumber { get; set; } = "";
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public DateOnly PayDate { get; set; }
    public PayFrequency Frequency { get; set; }
    public PayrollRunStatus Status { get; set; } = PayrollRunStatus.Draft;

    /// <summary>
    /// The attendance period this run was computed from, or null if it was computed without
    /// attendance. Explicit rather than matched by date, because periods can overlap.
    /// </summary>
    public Guid? AttendancePeriodId { get; set; }

    /// <summary>Employees in this run that had no attendance record, treated as fully present.</summary>
    public int EmployeesMissingAttendance { get; set; }

    public string PeriodLabel =>
        $"{PeriodStart:MMM d} – {PeriodEnd:MMM d, yyyy}";

    public List<PayrollRunEmployee> Employees { get; set; } = [];

    public int EmployeeCount => Employees.Count;
    public decimal TotalGrossPay => Employees.Sum(e => e.GrossPay);
    public decimal TotalDeductions => Employees.Sum(e => e.TotalDeductions);
    public decimal TotalNetPay => Employees.Sum(e => e.NetPay);
}
