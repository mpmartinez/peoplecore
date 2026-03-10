using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Organization;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Organization;

public class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Name).IsRequired().HasMaxLength(200);
        builder.HasOne(t => t.Department)
               .WithMany(d => d.Teams)
               .HasForeignKey(t => t.DepartmentId);
        builder.ToTable("teams");
    }
}
