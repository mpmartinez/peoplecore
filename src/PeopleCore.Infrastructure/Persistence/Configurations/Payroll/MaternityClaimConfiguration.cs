using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Payroll;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Payroll;

public class MaternityClaimConfiguration : IEntityTypeConfiguration<MaternityClaim>
{
    public void Configure(EntityTypeBuilder<MaternityClaim> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.DailyAllowance).HasColumnType("numeric(18,2)");
        builder.Property(x => x.Days).HasColumnType("numeric(6,2)");
        builder.Property(x => x.Benefit).HasColumnType("numeric(18,2)").HasDefaultValue(0m);
        builder.Property(x => x.ReimbursedAmount).HasColumnType("numeric(18,2)");
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(40);
        builder.Property(x => x.Note).HasMaxLength(500);

        // One claim per maternity leave request.
        builder.HasIndex(x => x.LeaveRequestId).IsUnique();

        builder.HasOne(x => x.LeaveRequest)
               .WithMany()
               .HasForeignKey(x => x.LeaveRequestId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Employee)
               .WithMany()
               .HasForeignKey(x => x.EmployeeId)
               .OnDelete(DeleteBehavior.Restrict);

        // Deleting the advance run leaves the claim in place, no longer pointing at it.
        builder.HasOne(x => x.AdvanceRun)
               .WithMany()
               .HasForeignKey(x => x.AdvanceRunId)
               .OnDelete(DeleteBehavior.SetNull);
    }
}
