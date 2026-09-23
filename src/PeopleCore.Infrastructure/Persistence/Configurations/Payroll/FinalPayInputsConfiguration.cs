using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Payroll;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Payroll;

public class FinalPayInputsConfiguration : IEntityTypeConfiguration<FinalPayInputs>
{
    public void Configure(EntityTypeBuilder<FinalPayInputs> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.WorkingDays).HasColumnType("numeric(6,2)");
        builder.Property(x => x.SeparationPayOverride).HasColumnType("numeric(18,2)");
        builder.Property(x => x.RetirementPayOverride).HasColumnType("numeric(18,2)");
        builder.Property(x => x.OverrideNote).HasMaxLength(500);

        // One-to-one with PayrollRun is configured on PayrollRunConfiguration, the principal side.

        builder.HasMany(x => x.Deductions)
               .WithOne(x => x.FinalPayInputs)
               .HasForeignKey(x => x.FinalPayInputsId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

public class FinalPayDeductionConfiguration : IEntityTypeConfiguration<FinalPayDeduction>
{
    public void Configure(EntityTypeBuilder<FinalPayDeduction> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Label).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Amount).HasColumnType("numeric(18,2)");
    }
}
