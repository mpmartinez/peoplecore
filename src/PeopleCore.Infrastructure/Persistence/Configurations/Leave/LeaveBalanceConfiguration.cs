using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Leave;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Leave;

public class LeaveBalanceConfiguration : IEntityTypeConfiguration<LeaveBalance>
{
    public void Configure(EntityTypeBuilder<LeaveBalance> builder)
    {
        builder.HasKey(b => b.Id);
        builder.HasIndex(b => new { b.EmployeeId, b.LeaveTypeId, b.Year }).IsUnique();
        builder.Property(b => b.TotalDays).HasPrecision(5, 2);
        builder.Property(b => b.UsedDays).HasPrecision(5, 2);
        builder.Property(b => b.CarriedOverDays).HasPrecision(5, 2);
        builder.Ignore(b => b.RemainingDays);
        builder.ToTable("leave_balances");
    }
}
