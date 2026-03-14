using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Leave;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Leave;

public class LeaveAccrualTransactionConfiguration : IEntityTypeConfiguration<LeaveAccrualTransaction>
{
    public void Configure(EntityTypeBuilder<LeaveAccrualTransaction> builder)
    {
        builder.HasKey(t => t.Id);

        builder.HasOne(t => t.Employee)
            .WithMany()
            .HasForeignKey(t => t.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.LeaveType)
            .WithMany()
            .HasForeignKey(t => t.LeaveTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => new { t.EmployeeId, t.LeaveTypeId, t.PeriodYear, t.PeriodMonth })
            .IsUnique();

        builder.Property(t => t.DaysAccrued).HasPrecision(5, 2);

        builder.ToTable("leave_accrual_transactions");
    }
}
