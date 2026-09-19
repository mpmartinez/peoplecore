namespace PeopleCore.Domain.Enums;

/// <summary>Where an attendance correction came from.</summary>
public enum AttendanceCorrectionSource { HrEdit, Import, EmployeeRequest }

/// <summary>Pending waits for an approver; Applied changed the day; Rejected changed nothing.</summary>
public enum AttendanceCorrectionStatus { Pending, Applied, Rejected }
