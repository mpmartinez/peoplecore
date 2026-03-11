namespace PeopleCore.Domain.Entities.Attendance;

public class AttendanceDevice : AuditableEntity
{
    public string Name { get; set; } = string.Empty;        // e.g. "Main Entrance"
    public string IpAddress { get; set; } = string.Empty;   // e.g. "192.168.1.201"
    public int Port { get; set; } = 4370;                   // ZKTeco default
    public string Protocol { get; set; } = "ZKTeco";        // ZKTeco | HTTP | ADMS
    public bool IsActive { get; set; } = true;
    public DateTime? LastSyncAt { get; set; }
    public string? Location { get; set; }
}
