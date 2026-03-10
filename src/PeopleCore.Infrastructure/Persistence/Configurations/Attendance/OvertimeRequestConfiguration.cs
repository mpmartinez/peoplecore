using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Attendance;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Attendance;

public class OvertimeRequestConfiguration : IEntityTypeConfiguration<OvertimeRequest>
{
    public void Configure(EntityTypeBuilder<OvertimeRequest> builder)
    {
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Status).HasConversion<string>();
        builder.HasOne(o => o.Employee)
               .WithMany()
               .HasForeignKey(o => o.EmployeeId)
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(o => o.Approver)
               .WithMany()
               .HasForeignKey(o => o.ApprovedBy)
               .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("overtime_requests");
    }
}
