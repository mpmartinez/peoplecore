using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Recruitment;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Recruitment;

public class ApplicantConfiguration : IEntityTypeConfiguration<Applicant>
{
    public void Configure(EntityTypeBuilder<Applicant> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Status).HasConversion<string>();
        builder.Property(a => a.Email).IsRequired().HasMaxLength(200);
        builder.ToTable("applicants");
    }
}
