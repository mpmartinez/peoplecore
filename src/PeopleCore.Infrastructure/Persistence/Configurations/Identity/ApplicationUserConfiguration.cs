using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Infrastructure.Identity;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Identity;

public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(u => u.FirstName).HasMaxLength(ApplicationUser.NameMaxLength);
        builder.Property(u => u.LastName).HasMaxLength(ApplicationUser.NameMaxLength);
    }
}
