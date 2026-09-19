using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Attendance;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Attendance;

public class AttendanceCorrectionConfiguration : IEntityTypeConfiguration<AttendanceCorrection>
{
    public void Configure(EntityTypeBuilder<AttendanceCorrection> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Source).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Reason).IsRequired().HasMaxLength(500);
        builder.Property(c => c.RequestedBy).IsRequired().HasMaxLength(256);
        builder.Property(c => c.ReviewedBy).HasMaxLength(256);
        builder.Property(c => c.RejectionReason).HasMaxLength(500);
        builder.HasIndex(c => new { c.EmployeeId, c.AttendanceDate });
        builder.HasIndex(c => c.Status);
        builder.HasOne(c => c.Employee)
               .WithMany()
               .HasForeignKey(c => c.EmployeeId)
               .OnDelete(DeleteBehavior.Cascade);
        builder.ToTable("attendance_corrections");
    }
}
