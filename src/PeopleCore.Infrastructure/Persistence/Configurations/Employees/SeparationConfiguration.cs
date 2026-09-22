using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Employees;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Employees;

public class SeparationConfiguration : IEntityTypeConfiguration<Separation>
{
    public void Configure(EntityTypeBuilder<Separation> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.AuthorizedCause).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Reason).HasMaxLength(1000);
        builder.Property(x => x.RecordedBy).HasMaxLength(256);
        builder.Property(x => x.SeparatedBy).HasMaxLength(256);
        builder.Ignore(x => x.FinalPayDueBy);
        builder.Ignore(x => x.ClearanceComplete);

        // At most one separation per employee: a withdrawn one is deleted, not kept.
        builder.HasIndex(x => x.EmployeeId).IsUnique();

        builder.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.ClearanceItems).WithOne(i => i.Separation).HasForeignKey(i => i.SeparationId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

public class SeparationClearanceItemConfiguration : IEntityTypeConfiguration<SeparationClearanceItem>
{
    public void Configure(EntityTypeBuilder<SeparationClearanceItem> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ClearedBy).HasMaxLength(256);
        builder.Property(x => x.Note).HasMaxLength(500);
    }
}
