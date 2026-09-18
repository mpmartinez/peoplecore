using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class Repository<T> : IRepository<T> where T : class
{
    protected readonly AppDbContext Context;
    protected readonly DbSet<T> DbSet;

    public Repository(AppDbContext context)
    {
        Context = context;
        DbSet = context.Set<T>();
    }

    public virtual async Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await DbSet.FindAsync([id], ct);

    public async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default)
        => await DbSet.ToListAsync(ct);

    public async Task<T> AddAsync(T entity, CancellationToken ct = default)
    {
        await DbSet.AddAsync(entity, ct);
        await Context.SaveChangesAsync(ct);
        return entity;
    }

    /// <summary>
    /// Saves changes to an entity, whether the context is already tracking it or not.
    /// <para>
    /// A detached entity (one mapped from a DTO, say) is attached with <c>Update</c>, which marks
    /// it and everything it reaches as existing rows to be updated.
    /// </para>
    /// <para>
    /// A tracked entity - the usual case, loaded and edited in the same scope - is saved as it
    /// stands. Every entity gets its Guid key when it is constructed, so to EF a new child added to
    /// a loaded collection looks exactly like an existing row: the <c>DetectChanges</c> that
    /// <c>SaveChanges</c> runs marks it Modified, and the insert goes out as an UPDATE that matches
    /// no row and fails with a concurrency error. But a child that sits in a collection of a
    /// tracked entity and is not itself tracked cannot be an existing row - loading the collection
    /// would have tracked it - so it is marked Added here, before <c>DetectChanges</c> gets to guess.
    /// </para>
    /// <para>
    /// Limits: only one-to-many collections are walked, from the entity down, so a new child hung
    /// off a reference navigation is not found. And an existing row that was loaded untracked and
    /// then put into a collection would be inserted again (a duplicate key); nothing loads rows
    /// untracked today.
    /// </para>
    /// </summary>
    public async Task UpdateAsync(T entity, CancellationToken ct = default)
    {
        // Entry() would otherwise run DetectChanges on the entity and mark its new children
        // Modified before they can be looked at.
        var autoDetect = Context.ChangeTracker.AutoDetectChangesEnabled;
        Context.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            var entry = Context.Entry(entity);
            if (entry.State == EntityState.Detached)
                DbSet.Update(entity);
            else
                AddUntrackedChildren(entry, new HashSet<object>(ReferenceEqualityComparer.Instance));
        }
        finally
        {
            Context.ChangeTracker.AutoDetectChangesEnabled = autoDetect;
        }

        await Context.SaveChangesAsync(ct);
    }

    private void AddUntrackedChildren(EntityEntry parent, HashSet<object> visited)
    {
        if (!visited.Add(parent.Entity))
            return;

        foreach (var collection in parent.Collections)
        {
            // Only one-to-many collections hold dependents the parent owns. An untracked entity in
            // a many-to-many collection is more likely an existing row being linked than a new one.
            if (collection.Metadata is not INavigation || collection.CurrentValue is null)
                continue;

            foreach (var child in collection.CurrentValue.Cast<object>().ToList())
            {
                var childEntry = Context.Entry(child);
                if (childEntry.State == EntityState.Detached)
                    childEntry.State = EntityState.Added;

                AddUntrackedChildren(childEntry, visited);
            }
        }
    }

    public async Task DeleteAsync(T entity, CancellationToken ct = default)
    {
        DbSet.Remove(entity);
        await Context.SaveChangesAsync(ct);
    }

    public async Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default)
        => predicate is null
            ? await DbSet.CountAsync(ct)
            : await DbSet.CountAsync(predicate, ct);
}
