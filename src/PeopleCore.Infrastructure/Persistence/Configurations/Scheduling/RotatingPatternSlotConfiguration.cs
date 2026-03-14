using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Scheduling;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Scheduling;

public class RotatingPatternSlotConfiguration : IEntityTypeConfiguration<RotatingPatternSlot>
{
    public void Configure(EntityTypeBuilder<RotatingPatternSlot> builder)
    {
        builder.ToTable("rotating_pattern_slots");
        builder.HasKey(e => e.Id);
        builder.HasOne(e => e.ShiftTemplate)
            .WithMany()
            .HasForeignKey(e => e.ShiftTemplateId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
