using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Scheduling;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Scheduling;

public class RotatingPatternConfiguration : IEntityTypeConfiguration<RotatingPattern>
{
    public void Configure(EntityTypeBuilder<RotatingPattern> builder)
    {
        builder.ToTable("rotating_patterns");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).HasMaxLength(100).IsRequired();
        builder.HasMany(e => e.Slots)
            .WithOne(s => s.RotatingPattern)
            .HasForeignKey(s => s.RotatingPatternId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
