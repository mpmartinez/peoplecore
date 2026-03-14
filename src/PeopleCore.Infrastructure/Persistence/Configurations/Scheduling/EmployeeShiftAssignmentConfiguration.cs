using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Scheduling;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Scheduling;

public class EmployeeShiftAssignmentConfiguration : IEntityTypeConfiguration<EmployeeShiftAssignment>
{
    public void Configure(EntityTypeBuilder<EmployeeShiftAssignment> builder)
    {
        builder.ToTable("employee_shift_assignments");
        builder.HasKey(e => e.Id);
        builder.HasOne(e => e.Employee)
            .WithMany()
            .HasForeignKey(e => e.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.ShiftTemplate)
            .WithMany()
            .HasForeignKey(e => e.ShiftTemplateId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(e => e.RotatingPattern)
            .WithMany()
            .HasForeignKey(e => e.RotatingPatternId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
        builder.Ignore(e => e.IsValid);
    }
}
