using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Leave;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Leave;

public class LeaveRequestConfiguration : IEntityTypeConfiguration<LeaveRequest>
{
    public void Configure(EntityTypeBuilder<LeaveRequest> builder)
    {
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Status).HasConversion<string>();
        builder.Property(l => l.TotalDays).HasPrecision(5, 2);

        builder.Property(l => l.MaternityCase).HasConversion<string>().HasMaxLength(40);
        builder.Property(l => l.DaysAllocatedToFather).HasDefaultValue(0);
        builder.Property(l => l.DaysInStartYear).HasPrecision(6, 2).HasDefaultValue(0m);
        builder.Property(l => l.DocumentFileName).HasMaxLength(255);
        builder.Property(l => l.DocumentStorageKey).HasMaxLength(500);
        builder.Property(l => l.DocumentContentType).HasMaxLength(100);

        builder.HasOne(l => l.Employee)
               .WithMany()
               .HasForeignKey(l => l.EmployeeId)
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(l => l.Approver)
               .WithMany()
               .HasForeignKey(l => l.ApprovedBy)
               .OnDelete(DeleteBehavior.Restrict);
        // A used type is retired, not deleted (LeaveTypeService refuses); the key backs that up
        // rather than cascading the employee's leave history away with the type.
        builder.HasOne(l => l.LeaveType)
               .WithMany()
               .HasForeignKey(l => l.LeaveTypeId)
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(l => new { l.EmployeeId, l.StartDate, l.EndDate });
        builder.ToTable("leave_requests");
    }
}
