using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Common.Auditing;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Domain.Entities;
using PeopleCore.Domain.Entities.Attendance;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Entities.Organization;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Entities.Performance;
using PeopleCore.Domain.Entities.Recruitment;
using PeopleCore.Domain.Entities.Scheduling;
using PeopleCore.Domain.Entities.System;
using PeopleCore.Domain.Enums;
using PeopleCore.Infrastructure.Auditing;
using PeopleCore.Infrastructure.Identity;

namespace PeopleCore.Infrastructure.Persistence;

public class AppDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>, IDataProtectionKeyContext
{
    private readonly ICurrentUserService? _currentUser;
    private readonly IAuditContext? _auditContext;

    /// <summary>Set while the audit entries of a save are written, so that save is not audited in turn.</summary>
    private bool _writingAuditTrail;

    // currentUser and auditContext are optional so design-time tooling and the startup seeder,
    // which have no HTTP context, can still construct the context. Audit columns are left null in
    // that case, and the audit trail names the actor "System".
    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        ICurrentUserService? currentUser = null,
        IAuditContext? auditContext = null)
        : base(options)
    {
        _currentUser = currentUser;
        _auditContext = auditContext;
    }

    // Organization
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<Team> Teams => Set<Team>();

    // Employees
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<EmployeeGovernmentId> EmployeeGovernmentIds => Set<EmployeeGovernmentId>();
    public DbSet<EmergencyContact> EmergencyContacts => Set<EmergencyContact>();
    public DbSet<EmployeeDocument> EmployeeDocuments => Set<EmployeeDocument>();
    public DbSet<Separation> Separations => Set<Separation>();
    public DbSet<SeparationClearanceItem> SeparationClearanceItems => Set<SeparationClearanceItem>();

    // Attendance
    public DbSet<Holiday> Holidays => Set<Holiday>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<OvertimeRequest> OvertimeRequests => Set<OvertimeRequest>();
    public DbSet<AttendanceCorrection> AttendanceCorrections => Set<AttendanceCorrection>();
    public DbSet<AttendanceDevice> AttendanceDevices { get; set; } = null!;

    // Leave
    public DbSet<LeaveType> LeaveTypes => Set<LeaveType>();
    public DbSet<LeaveBalance> LeaveBalances => Set<LeaveBalance>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();
    public DbSet<LeaveAccrualPolicy> LeaveAccrualPolicies => Set<LeaveAccrualPolicy>();
    public DbSet<LeaveAccrualTransaction> LeaveAccrualTransactions => Set<LeaveAccrualTransaction>();

    // Payroll
    public DbSet<EmployeeCompensation> EmployeeCompensations => Set<EmployeeCompensation>();
    public DbSet<EmployeeAllowance> EmployeeAllowances => Set<EmployeeAllowance>();
    public DbSet<EmployeeLoan> EmployeeLoans => Set<EmployeeLoan>();
    public DbSet<PayrollRun> PayrollRuns => Set<PayrollRun>();
    public DbSet<PayrollRunEmployee> PayrollRunEmployees => Set<PayrollRunEmployee>();
    public DbSet<PayrollLoanDeduction> PayrollLoanDeductions => Set<PayrollLoanDeduction>();
    public DbSet<PayrollRunPremiumDay> PayrollRunPremiumDays => Set<PayrollRunPremiumDay>();
    public DbSet<Bir2316Inputs> Bir2316Inputs => Set<Bir2316Inputs>();
    public DbSet<PayrollSettings> PayrollSettings => Set<PayrollSettings>();
    public DbSet<FinalPayInputs> FinalPayInputs => Set<FinalPayInputs>();
    public DbSet<FinalPayDeduction> FinalPayDeductions => Set<FinalPayDeduction>();
    public DbSet<MaternityClaim> MaternityClaims => Set<MaternityClaim>();
    public DbSet<PayrollOpeningBalance> PayrollOpeningBalances => Set<PayrollOpeningBalance>();

    // Recruitment
    public DbSet<JobPosting> JobPostings => Set<JobPosting>();
    public DbSet<Applicant> Applicants => Set<Applicant>();
    public DbSet<InterviewStage> InterviewStages => Set<InterviewStage>();

    // Performance
    public DbSet<ReviewCycle> ReviewCycles => Set<ReviewCycle>();
    public DbSet<PerformanceReview> PerformanceReviews => Set<PerformanceReview>();
    public DbSet<KpiItem> KpiItems => Set<KpiItem>();

    // Scheduling
    public DbSet<ShiftTemplate> ShiftTemplates => Set<ShiftTemplate>();
    public DbSet<RotatingPattern> RotatingPatterns => Set<RotatingPattern>();
    public DbSet<RotatingPatternSlot> RotatingPatternSlots => Set<RotatingPatternSlot>();
    public DbSet<EmployeeShiftAssignment> ShiftAssignments => Set<EmployeeShiftAssignment>();

    /// <summary>The Data Protection key ring. In the database so reset links survive a restart.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<EmailSettings> EmailSettings => Set<EmailSettings>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    // The parameterless SaveChanges overloads both end up here, as do Identity's stores.
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        if (_writingAuditTrail)
            return base.SaveChanges(acceptAllChangesOnSuccess);

        AuditChangeCollector.RefuseAuditLogChanges(ChangeTracker);
        StampAuditColumns();
        ChangeTracker.DetectChanges();
        var pending = AuditChangeCollector.Collect(ChangeTracker);
        if (pending.Count == 0)
            return base.SaveChanges(acceptAllChangesOnSuccess);

        // Usually every key is known before the insert, and the entries go out in the same save as
        // the change. Not when acceptAllChangesOnSuccess is false: the second save below would
        // write the change again.
        if (!acceptAllChangesOnSuccess || !pending.Any(p => p.KeyIsGenerated))
        {
            AddAuditEntries(pending);
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        // A key the database generates is known only after the insert, so the entries follow in a
        // second save. The change and its entries still commit together: a transaction the caller
        // opened is joined, otherwise this save opens its own.
        using var tx = Database.CurrentTransaction is null ? Database.BeginTransaction() : null;
        var saved = base.SaveChanges(acceptAllChangesOnSuccess);
        WriteAuditEntries(pending);
        tx?.Commit();
        return saved;
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        if (_writingAuditTrail)
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

        AuditChangeCollector.RefuseAuditLogChanges(ChangeTracker);
        StampAuditColumns();
        ChangeTracker.DetectChanges();
        var pending = await AuditChangeCollector.CollectAsync(ChangeTracker, cancellationToken);
        if (pending.Count == 0)
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

        if (!acceptAllChangesOnSuccess || !pending.Any(p => p.KeyIsGenerated))
        {
            AddAuditEntries(pending);
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        await using var tx = Database.CurrentTransaction is null
            ? await Database.BeginTransactionAsync(cancellationToken)
            : null;
        var saved = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        await WriteAuditEntriesAsync(pending, cancellationToken);
        if (tx is not null)
            await tx.CommitAsync(cancellationToken);
        return saved;
    }

    /// <summary>
    /// Records a change made outside the change tracker - an <c>ExecuteUpdate</c> or
    /// <c>ExecuteDelete</c> - with the context's actor. Saved by the next SaveChanges.
    /// </summary>
    public void RecordUntrackedChange(
        string action, string entityType, string? entityId, string summary,
        Guid? subjectEmployeeId = null,
        IReadOnlyList<AuditFieldChange>? changes = null,
        object? metadata = null)
    {
        AuditLogs.Add(AuditActor.Stamp(new AuditLog
        {
            Category = AuditCategory.DataChange,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Summary = summary,
            SubjectEmployeeId = subjectEmployeeId,
            Changes = AuditJson.Serialize(changes),
            Metadata = AuditJson.Serialize(metadata),
        }, _auditContext));
    }

    // The second save of a change with a generated key (role permissions have an identity key):
    // by now the key is assigned and the change's own entries are no longer pending.
    private void WriteAuditEntries(List<PendingDataChange> pending)
    {
        AddAuditEntries(pending);
        _writingAuditTrail = true;
        try { base.SaveChanges(true); }
        finally { _writingAuditTrail = false; }
    }

    private async Task WriteAuditEntriesAsync(List<PendingDataChange> pending, CancellationToken ct)
    {
        AddAuditEntries(pending);
        _writingAuditTrail = true;
        try { await base.SaveChangesAsync(true, ct); }
        finally { _writingAuditTrail = false; }
    }

    private void AddAuditEntries(List<PendingDataChange> pending)
    {
        var autoDetect = ChangeTracker.AutoDetectChangesEnabled;
        ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            foreach (var change in pending)
                AuditLogs.Add(change.ToAuditLog(_auditContext));
        }
        finally
        {
            ChangeTracker.AutoDetectChangesEnabled = autoDetect;
        }
    }

    private void StampAuditColumns()
    {
        var userId = _currentUser?.UserId;
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<IAuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.CreatedBy = userId;
                entry.Entity.UpdatedAt = now;
                entry.Entity.UpdatedBy = userId;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Entity.UpdatedBy = userId;

                // Never let an update rewrite who created the row.
                entry.Property(e => e.CreatedAt).IsModified = false;
                entry.Property(e => e.CreatedBy).IsModified = false;
            }
        }
    }
}
