using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using PeopleCore.Application.Common.Auditing;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.System;
using PeopleCore.Domain.Enums;
using PeopleCore.Infrastructure.Identity;

namespace PeopleCore.Infrastructure.Auditing;

/// <summary>
/// A data change caught before it is saved. The field values are read now, while the tracker still
/// has them; the key is read after the save, once the database has assigned any it generates.
/// </summary>
internal sealed class PendingDataChange
{
    public required EntityEntry Entry { get; init; }
    public required EntityState State { get; init; }
    public required string EntityType { get; init; }
    public required IReadOnlyList<AuditFieldChange> Changes { get; init; }
    public required Guid? SubjectEmployeeId { get; init; }

    /// <summary>A deleted row's key, read before the save detaches it.</summary>
    public string? KnownKey { get; init; }

    /// <summary>The key is the database's to assign, so it is unknown until the insert runs.</summary>
    public bool KeyIsGenerated { get; init; }

    public AuditLog ToAuditLog(IAuditContext? context)
    {
        var key = KnownKey ?? AuditChangeCollector.KeyOf(Entry);
        var log = new AuditLog
        {
            Category = AuditCategory.DataChange,
            Action = State switch
            {
                EntityState.Added => "Created",
                EntityState.Deleted => "Deleted",
                _ => "Updated",
            },
            EntityType = EntityType,
            EntityId = key,
            SubjectEmployeeId = SubjectEmployeeId,
            Summary = $"{EntityType} {State switch { EntityState.Added => "created", EntityState.Deleted => "deleted", _ => "updated" }}",
            Changes = AuditJson.Serialize(Changes),
        };
        return AuditActor.Stamp(log, context);
    }
}

/// <summary>
/// Turns the change tracker's inserts, updates and deletes into audit entries, field by field.
/// </summary>
internal static class AuditChangeCollector
{
    public const int MaxStringLength = 4000;
    public const string RedactedValue = "(changed)";
    public const string BinaryValue = "(binary)";

    /// <summary>Not recorded at all: the trail itself, the key ring, and Identity's stored tokens.</summary>
    private static readonly HashSet<Type> ExcludedTypes =
    [
        typeof(AuditLog),
        typeof(DataProtectionKey),
        typeof(IdentityUserToken<string>),
    ];

    /// <summary>Who and when are on the audit entry itself; repeating them in the changes is noise.</summary>
    private static readonly HashSet<string> SkippedProperties =
        ["CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy"];

    /// <summary>Recorded only as having changed. Their values are secrets, or worthless to a reader.</summary>
    private static readonly HashSet<string> RedactedProperties =
        ["PasswordHash", "SecurityStamp", "ConcurrencyStamp", "PasswordProtected"];

    private static readonly Dictionary<Type, string> FriendlyNames = new()
    {
        [typeof(ApplicationUser)] = "UserAccount",
        [typeof(ApplicationRole)] = "Role",
        [typeof(IdentityUserRole<string>)] = "UserRole",
        [typeof(IdentityRoleClaim<string>)] = "RolePermission",
        [typeof(IdentityUserClaim<string>)] = "UserClaim",
        [typeof(IdentityUserLogin<string>)] = "UserLogin",
    };

    public static async Task<List<PendingDataChange>> CollectAsync(ChangeTracker tracker, CancellationToken ct)
    {
        var pending = new List<PendingDataChange>();
        foreach (var entry in tracker.Entries().ToList())
        {
            if (!IsAudited(entry)) continue;

            PropertyValues? databaseValues = null;
            if (entry.State == EntityState.Modified && LooksAttachedByUpdate(entry))
                databaseValues = await entry.GetDatabaseValuesAsync(ct);

            Add(pending, entry, databaseValues);
        }
        return pending;
    }

    public static List<PendingDataChange> Collect(ChangeTracker tracker)
    {
        var pending = new List<PendingDataChange>();
        foreach (var entry in tracker.Entries().ToList())
        {
            if (!IsAudited(entry)) continue;

            PropertyValues? databaseValues = null;
            if (entry.State == EntityState.Modified && LooksAttachedByUpdate(entry))
                databaseValues = entry.GetDatabaseValues();

            Add(pending, entry, databaseValues);
        }
        return pending;
    }

    /// <summary>Throws if anything would rewrite or remove an audit entry.</summary>
    public static void RefuseAuditLogChanges(ChangeTracker tracker)
    {
        if (tracker.Entries<AuditLog>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("The audit trail is append-only: an audit entry cannot be changed or deleted.");
    }

    public static string EntityTypeName(Type type) =>
        FriendlyNames.TryGetValue(type, out var name) ? name : type.Name;

    public static string? KeyOf(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null) return null;
        var parts = key.Properties.Select(p => entry.Property(p.Name).CurrentValue?.ToString() ?? "");
        return string.Join('|', parts);
    }

    private static bool IsAudited(EntityEntry entry) =>
        entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
        && !ExcludedTypes.Contains(entry.Metadata.ClrType);

    /// <summary>
    /// <c>DbSet.Update</c> on a detached entity - what <c>Repository.UpdateAsync</c> does with one
    /// mapped from a DTO - marks every property modified and makes its original values equal to the
    /// current ones, so the tracker no longer knows what changed. Those rows are diffed against the
    /// database instead.
    /// </summary>
    private static bool LooksAttachedByUpdate(EntityEntry entry)
    {
        var properties = entry.Properties
            .Where(p => !p.Metadata.IsPrimaryKey() && !SkippedProperties.Contains(p.Metadata.Name))
            .ToList();
        return properties.Count > 0
            && properties.All(p => p.IsModified)
            && properties.All(p => Equals(p.OriginalValue, p.CurrentValue));
    }

    private static void Add(List<PendingDataChange> pending, EntityEntry entry, PropertyValues? databaseValues)
    {
        var changes = new List<AuditFieldChange>();
        foreach (var property in entry.Properties)
        {
            var name = property.Metadata.Name;
            if (SkippedProperties.Contains(name)) continue;

            switch (entry.State)
            {
                case EntityState.Added:
                    if (property.CurrentValue is null || property.IsTemporary) continue;
                    changes.Add(new(name, null, Format(name, property.CurrentValue)));
                    break;

                case EntityState.Deleted:
                    changes.Add(new(name, Format(name, property.OriginalValue), null));
                    break;

                case EntityState.Modified:
                    if (property.Metadata.IsPrimaryKey()) continue;
                    var before = databaseValues is not null ? databaseValues[property.Metadata] : property.OriginalValue;
                    var after = property.CurrentValue;
                    if (!property.IsModified && databaseValues is null) continue;
                    if (ValuesEqual(before, after)) continue;
                    changes.Add(new(name, Format(name, before), Format(name, after)));
                    break;
            }
        }

        // An update that changes nothing (a form saved as it was) is not a change.
        if (entry.State == EntityState.Modified && changes.Count == 0) return;

        pending.Add(new PendingDataChange
        {
            Entry = entry,
            State = entry.State,
            EntityType = EntityTypeName(entry.Metadata.ClrType),
            Changes = changes,
            SubjectEmployeeId = SubjectEmployeeOf(entry),
            KnownKey = entry.State == EntityState.Deleted ? KeyOf(entry) : null,
            KeyIsGenerated = entry.Metadata.FindPrimaryKey()?.Properties
                .Any(p => entry.Property(p.Name).IsTemporary) ?? false,
        });
    }

    private static bool ValuesEqual(object? a, object? b) =>
        a is byte[] x && b is byte[] y ? x.AsSpan().SequenceEqual(y) : Equals(a, b);

    /// <summary>
    /// The employee an entry is about: an employee row itself, or any row with an <c>EmployeeId</c> -
    /// most HR and payroll rows, a separation, a sign-in account.
    /// </summary>
    private static Guid? SubjectEmployeeOf(EntityEntry entry)
    {
        if (entry.Entity is Employee employee) return employee.Id;

        var property = entry.Metadata.FindProperty("EmployeeId");
        if (property is null) return null;

        var value = entry.State == EntityState.Deleted
            ? entry.Property(property.Name).OriginalValue
            : entry.Property(property.Name).CurrentValue;
        return value as Guid?;
    }

    private static object? Format(string property, object? value)
    {
        if (value is null) return null;
        if (RedactedProperties.Contains(property)) return RedactedValue;
        return value switch
        {
            byte[] => BinaryValue,
            Enum e => e.ToString(),
            string s when s.Length > MaxStringLength => s[..MaxStringLength] + "…",
            _ => value,
        };
    }
}
