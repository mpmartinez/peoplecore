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
        builder.HasOne(l => l.Employee)
               .WithMany()
               .HasForeignKey(l => l.EmployeeId)
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(l => l.Approver)
               .WithMany()
               .HasForeignKey(l => l.ApprovedBy)
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(l => new { l.EmployeeId, l.StartDate, l.EndDate });
        builder.ToTable("leave_requests");
    }
}
