using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Leave;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Leave;

public class LeaveAccrualPolicyConfiguration : IEntityTypeConfiguration<LeaveAccrualPolicy>
{
    public void Configure(EntityTypeBuilder<LeaveAccrualPolicy> builder)
    {
        builder.HasKey(p => p.Id);

        builder.HasOne(p => p.LeaveType)
            .WithMany()
            .HasForeignKey(p => p.LeaveTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(p => p.DaysPerYear).HasPrecision(5, 2);

        builder.ToTable("leave_accrual_policies");
    }
}
