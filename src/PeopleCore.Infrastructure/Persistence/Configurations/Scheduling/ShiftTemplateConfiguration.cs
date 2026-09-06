using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Scheduling;
using PeopleCore.Domain.Enums;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Scheduling;

public class ShiftTemplateConfiguration : IEntityTypeConfiguration<ShiftTemplate>
{
    public void Configure(EntityTypeBuilder<ShiftTemplate> builder)
    {
        builder.ToTable("shift_templates");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).HasMaxLength(100).IsRequired();
        builder.Property(e => e.StartTime).IsRequired();
        builder.Property(e => e.EndTime).IsRequired();

        // Flags enum mapped to its underlying int - no value converter needed.
        builder.Property(e => e.WorkDays)
            .IsRequired()
            .HasDefaultValue(WorkDays.MondayToFriday);
    }
}
