namespace PeopleCore.Domain.Entities.Leave;

public class LeaveBalance : AuditableEntity
{
    public Guid EmployeeId { get; set; }
    public LeaveType LeaveType { get; set; } = null!;
    public Guid LeaveTypeId { get; set; }
    public int Year { get; set; }
    public decimal TotalDays { get; set; } = 0;
    public decimal UsedDays { get; set; } = 0;
    public decimal CarriedOverDays { get; set; } = 0;
    public decimal RemainingDays => TotalDays + CarriedOverDays - UsedDays;
}
