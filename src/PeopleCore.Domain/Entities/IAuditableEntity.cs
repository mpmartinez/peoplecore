namespace PeopleCore.Domain.Entities;

/// <summary>
/// Audit columns stamped by the persistence layer on save. Implemented by
/// <see cref="AuditableEntity"/> and, separately, by Employee — which cannot inherit it because
/// it already derives from the shared M2NET.Core base.
/// </summary>
public interface IAuditableEntity
{
    DateTime CreatedAt { get; set; }
    DateTime UpdatedAt { get; set; }
    Guid? CreatedBy { get; set; }
    Guid? UpdatedBy { get; set; }
}
