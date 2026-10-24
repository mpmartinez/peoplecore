using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.System;

namespace PeopleCore.Infrastructure.Persistence.Configurations.System;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).UseIdentityAlwaysColumn();

        builder.Property(a => a.Category).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Outcome).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Action).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Summary).HasMaxLength(500);
        builder.Property(a => a.FailureReason).HasMaxLength(1000);
        builder.Property(a => a.UserId).HasMaxLength(450);
        builder.Property(a => a.UserEmail).HasMaxLength(256);
        builder.Property(a => a.ActorName).HasMaxLength(200);
        builder.Property(a => a.IpAddress).HasMaxLength(64);
        builder.Property(a => a.UserAgent).HasMaxLength(500);
        builder.Property(a => a.CorrelationId).HasMaxLength(100);
        builder.Property(a => a.HttpMethod).HasMaxLength(10);
        builder.Property(a => a.Path).HasMaxLength(500);
        builder.Property(a => a.EntityType).HasMaxLength(100);
        builder.Property(a => a.EntityId).HasMaxLength(200);
        builder.Property(a => a.Changes).HasColumnType("jsonb");
        builder.Property(a => a.Metadata).HasColumnType("jsonb");

        builder.HasIndex(a => a.OccurredAt).IsDescending();
        builder.HasIndex(a => new { a.EntityType, a.EntityId });
        builder.HasIndex(a => a.SubjectEmployeeId);
        builder.HasIndex(a => a.UserId);
        builder.HasIndex(a => a.CorrelationId);
        builder.HasIndex(a => a.Category);
    }
}
