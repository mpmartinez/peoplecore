using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Performance;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Performance;

public class KpiItemConfiguration : IEntityTypeConfiguration<KpiItem>
{
    public void Configure(EntityTypeBuilder<KpiItem> builder)
    {
        builder.HasKey(k => k.Id);
        builder.Property(k => k.Description).IsRequired().HasMaxLength(500);
        builder.Property(k => k.Weight).HasPrecision(5, 2);
        builder.Property(k => k.Score).HasPrecision(5, 2);
        builder.HasOne(k => k.PerformanceReview)
               .WithMany(pr => pr.KpiItems)
               .HasForeignKey(k => k.PerformanceReviewId)
               .OnDelete(DeleteBehavior.Cascade);
        builder.ToTable("kpi_items");
    }
}
