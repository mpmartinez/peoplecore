namespace PeopleCore.Domain.Entities.Leave;

public class LeaveAccrualPolicy : AuditableEntity
{
    public Guid LeaveTypeId { get; set; }
    public LeaveType LeaveType { get; set; } = null!;
    public int TenureMonthsMin { get; set; }
    public int? TenureMonthsMax { get; set; }  // null = open-ended (e.g. 60+ months)
    public decimal DaysPerYear { get; set; }
    public AccrualFrequency AccrualFrequency { get; set; } = AccrualFrequency.Monthly;
    public bool IsActive { get; set; } = true;
}

public enum AccrualFrequency
{
    Monthly,
    Annual
}
