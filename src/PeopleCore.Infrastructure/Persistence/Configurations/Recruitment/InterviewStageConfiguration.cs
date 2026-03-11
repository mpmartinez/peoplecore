using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Recruitment;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Recruitment;

public class InterviewStageConfiguration : IEntityTypeConfiguration<InterviewStage>
{
    public void Configure(EntityTypeBuilder<InterviewStage> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.StageName).IsRequired().HasMaxLength(200);
        builder.HasOne(i => i.Applicant)
               .WithMany(a => a.InterviewStages)
               .HasForeignKey(i => i.ApplicantId)
               .OnDelete(DeleteBehavior.Cascade);
        builder.ToTable("interview_stages");
    }
}
