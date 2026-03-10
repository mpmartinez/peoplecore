using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Performance;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Performance;

public class PerformanceReviewConfiguration : IEntityTypeConfiguration<PerformanceReview>
{
    public void Configure(EntityTypeBuilder<PerformanceReview> builder)
    {
        builder.HasKey(pr => pr.Id);
        builder.HasIndex(pr => new { pr.EmployeeId, pr.ReviewCycleId }).IsUnique();
        builder.Property(pr => pr.Status).HasConversion<string>();
        builder.Property(pr => pr.SelfEvaluationScore).HasPrecision(5, 2);
        builder.Property(pr => pr.ManagerScore).HasPrecision(5, 2);
        builder.Property(pr => pr.FinalScore).HasPrecision(5, 2);
        builder.HasOne(pr => pr.Employee)
               .WithMany()
               .HasForeignKey(pr => pr.EmployeeId)
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(pr => pr.Reviewer)
               .WithMany()
               .HasForeignKey(pr => pr.ReviewerId)
               .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("performance_reviews");
    }
}
