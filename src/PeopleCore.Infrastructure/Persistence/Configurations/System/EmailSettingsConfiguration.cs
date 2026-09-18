using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.System;

namespace PeopleCore.Infrastructure.Persistence.Configurations.System;

public class EmailSettingsConfiguration : IEntityTypeConfiguration<EmailSettings>
{
    public void Configure(EntityTypeBuilder<EmailSettings> builder)
    {
        builder.ToTable("email_settings", t =>
            t.HasCheckConstraint("ck_email_settings_single_row", "id = 1"));

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Host).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Username).HasMaxLength(200);
        builder.Property(s => s.PasswordProtected).HasMaxLength(2000);
        builder.Property(s => s.FromAddress).HasMaxLength(200).IsRequired();
        builder.Property(s => s.FromName).HasMaxLength(100).IsRequired();
        builder.Property(s => s.AppBaseUrl).HasMaxLength(300).IsRequired();
    }
}
