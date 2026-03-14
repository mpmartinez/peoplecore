using PeopleCore.Domain.Entities.Employees;

namespace PeopleCore.Domain.Entities.Leave;

public class LeaveAccrualTransaction : AuditableEntity
{
    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public Guid LeaveTypeId { get; set; }
    public LeaveType LeaveType { get; set; } = null!;
    public DateOnly AccrualDate { get; set; }
    public decimal DaysAccrued { get; set; }
    public string PolicySnapshot { get; set; } = string.Empty;  // JSON audit trail
    public int PeriodYear { get; set; }
    public int PeriodMonth { get; set; }
}
