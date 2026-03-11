using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Performance;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Performance;

public class ReviewCycleConfiguration : IEntityTypeConfiguration<ReviewCycle>
{
    public void Configure(EntityTypeBuilder<ReviewCycle> builder)
    {
        builder.HasKey(rc => rc.Id);
        builder.Property(rc => rc.Name).IsRequired().HasMaxLength(200);
        builder.Property(rc => rc.Status).HasConversion<string>();
        builder.ToTable("review_cycles");
    }
}
