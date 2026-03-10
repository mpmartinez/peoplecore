using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Attendance;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Attendance;

public class AttendanceRecordConfiguration : IEntityTypeConfiguration<AttendanceRecord>
{
    public void Configure(EntityTypeBuilder<AttendanceRecord> builder)
    {
        builder.HasKey(a => a.Id);
        builder.HasIndex(a => new { a.EmployeeId, a.AttendanceDate }).IsUnique();
        builder.Property(a => a.HolidayType).HasConversion<string>();
        builder.ToTable("attendance_records");
    }
}
