using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Leave.DTOs;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;

namespace PeopleCore.Application.Leave.Services;

public class LeaveRequestService : ILeaveRequestService
{
    private readonly ILeaveRequestRepository _leaveRepo;
    private readonly ILeaveBalanceRepository _balanceRepo;
    private readonly ILeaveDayCounter _dayCounter;
    private readonly IEmployeeRepository _employeeRepo;
    private readonly ILeaveTypeRepository _leaveTypeRepo;

    public LeaveRequestService(
        ILeaveRequestRepository leaveRepo,
        ILeaveBalanceRepository balanceRepo,
        ILeaveDayCounter dayCounter,
        IEmployeeRepository employeeRepo,
        ILeaveTypeRepository leaveTypeRepo)
    {
        _leaveRepo = leaveRepo;
        _balanceRepo = balanceRepo;
        _dayCounter = dayCounter;
        _employeeRepo = employeeRepo;
        _leaveTypeRepo = leaveTypeRepo;
    }

    public async Task<PagedResult<LeaveRequestDto>> GetAllAsync(
        Guid? employeeId, Guid? reportingManagerId, string? status, int page, int pageSize, CancellationToken ct = default)
    {
        var (items, total) = await _leaveRepo.GetPagedAsync(employeeId, reportingManagerId, status, page, pageSize, ct);
        return PagedResult<LeaveRequestDto>.Create(items.Select(ToDto).ToList(), total, page, pageSize);
    }

    public async Task<LeaveRequestDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var request = await _leaveRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Leave request {id} not found.");
        return ToDto(request);
    }

    public async Task<LeaveRequestDto> CreateAsync(CreateLeaveRequestDto dto, CancellationToken ct = default)
    {
        var employee = await _employeeRepo.GetByIdAsync(dto.EmployeeId, ct)
            ?? throw new KeyNotFoundException($"Employee {dto.EmployeeId} not found.");

        var leaveType = await _leaveTypeRepo.GetByIdAsync(dto.LeaveTypeId, ct)
            ?? throw new KeyNotFoundException($"Leave type {dto.LeaveTypeId} not found.");

        // The maternity details only mean something on a maternity type.
        var maternityCase = leaveType.IsMaternity ? dto.MaternityCase : null;
        var daysAllocatedToFather = leaveType.IsMaternity ? dto.DaysAllocatedToFather : 0;

        var checkedRequest = await CheckAsync(
            employee, leaveType, dto.StartDate, dto.EndDate, maternityCase, daysAllocatedToFather, excludeId: null, ct);

        // A YearlyAllowance year's balance is created on first filing in that year.
        if (leaveType.EntitlementKind == LeaveEntitlementKind.YearlyAllowance)
        {
            foreach (var (year, days) in checkedRequest.DaysByYear)
            {
                if (days <= 0 || checkedRequest.Balances.ContainsKey(year))
                    continue;

                // Added explicitly - an AuditableEntity has its Guid from construction, so EF would
                // otherwise take a new row reached through a tracked parent for an UPDATE.
                await _balanceRepo.AddAsync(new LeaveBalance
                {
                    EmployeeId = employee.Id,
                    LeaveTypeId = leaveType.Id,
                    Year = year,
                    TotalDays = leaveType.MaxDaysPerYear
                }, ct);
            }
        }

        var request = new LeaveRequest
        {
            EmployeeId = dto.EmployeeId,
            LeaveTypeId = dto.LeaveTypeId,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            TotalDays = checkedRequest.TotalDays,
            DaysInStartYear = checkedRequest.DaysInStartYear,
            MaternityCase = maternityCase,
            DaysAllocatedToFather = daysAllocatedToFather,
            Reason = dto.Reason,
            Status = LeaveStatus.Pending
        };

        var created = await _leaveRepo.AddAsync(request, ct);
        return ToDto(created);
    }

    public async Task<LeaveRequestDto> ApproveAsync(Guid id, Guid approverId, CancellationToken ct = default)
    {
        var request = await _leaveRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Leave request {id} not found.");

        if (request.Status != LeaveStatus.Pending)
            throw new DomainException("Only pending leave requests can be approved.");

        EnsureNotOwnRequest(request, approverId, "approve");

        var employee = await _employeeRepo.GetByIdAsync(request.EmployeeId, ct)
            ?? throw new KeyNotFoundException($"Employee {request.EmployeeId} not found.");
        var leaveType = await _leaveTypeRepo.GetByIdAsync(request.LeaveTypeId, ct)
            ?? throw new KeyNotFoundException($"Leave type {request.LeaveTypeId} not found.");

        // Recount and re-run every check. The request itself holds nothing against its own approval.
        var checkedRequest = await CheckAsync(
            employee, leaveType, request.StartDate, request.EndDate,
            request.MaternityCase, request.DaysAllocatedToFather, excludeId: request.Id, ct);

        // Check 14, on approval only.
        if (leaveType.RequiresDocument && request.DocumentStorageKey is null)
            throw new DomainException($"{leaveType.Name} needs a supporting document.");

        if (leaveType.EntitlementKind != LeaveEntitlementKind.PerEvent)
        {
            var charges = checkedRequest.DaysByYear.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key).ToList();

            // An Accrued year with no row can't be charged. The balance check, which counts a missing
            // row as 0, refuses this first; kept (before any year is charged) so nothing is ever
            // charged to no row.
            var yearWithoutRow = charges
                .Where(kv => !checkedRequest.Balances.ContainsKey(kv.Key))
                .Select(kv => (int?)kv.Key)
                .FirstOrDefault();
            if (leaveType.EntitlementKind == LeaveEntitlementKind.Accrued && yearWithoutRow is { } missingYear)
                throw new DomainException($"You have 0 days of {leaveType.Name} left for {missingYear}.");

            foreach (var (year, days) in charges)
            {
                if (checkedRequest.Balances.TryGetValue(year, out var balance))
                {
                    balance.UsedDays += days;
                    balance.UpdatedAt = DateTime.UtcNow;
                    await _balanceRepo.UpdateAsync(balance, ct);
                }
                else
                {
                    // YearlyAllowance: created at filing, so only missing if deleted since; the
                    // allowance still applies. Added explicitly (the EF trap, as at filing).
                    await _balanceRepo.AddAsync(new LeaveBalance
                    {
                        EmployeeId = employee.Id,
                        LeaveTypeId = leaveType.Id,
                        Year = year,
                        TotalDays = leaveType.MaxDaysPerYear,
                        UsedDays = days
                    }, ct);
                }
            }
        }

        request.TotalDays = checkedRequest.TotalDays;
        request.DaysInStartYear = checkedRequest.DaysInStartYear;
        request.Status = LeaveStatus.Approved;
        request.ApprovedBy = approverId;
        request.ApprovedAt = DateTime.UtcNow;
        request.UpdatedAt = DateTime.UtcNow;

        await _leaveRepo.UpdateAsync(request, ct);
        return ToDto(request);
    }

    /// <summary>What <see cref="CheckAsync"/> found: the day counts and the balance rows it read, by year.</summary>
    private sealed record CheckedRequest(
        IReadOnlyDictionary<int, decimal> DaysByYear,
        decimal TotalDays,
        decimal DaysInStartYear,
        IReadOnlyDictionary<int, LeaveBalance> Balances);

    /// <summary>
    /// Counts the days and runs the filing checks in the Global Constraints order: eligibility
    /// (1-8), overlap (11), then limits (9, 10, 12, 13). A year's available days are its balance
    /// (YearlyAllowance with no row: the allowance; Accrued with no row: 0) less the days the
    /// employee's other Pending requests of the type hold there. <paramref name="excludeId"/>
    /// leaves the request being approved out of both the holds and the overlap check.
    /// </summary>
    private async Task<CheckedRequest> CheckAsync(
        Employee employee, LeaveType leaveType, DateOnly start, DateOnly end,
        MaternityCase? maternityCase, int daysAllocatedToFather, Guid? excludeId, CancellationToken ct)
    {
        var daysByYear = await _dayCounter.CountByYearAsync(employee.Id, leaveType, start, end, ct);
        var approvedEvents = await _leaveRepo.CountApprovedAsync(employee.Id, leaveType.Id, ct);

        var balances = new Dictionary<int, LeaveBalance>();
        var availableByYear = new Dictionary<int, decimal>();
        if (leaveType.EntitlementKind != LeaveEntitlementKind.PerEvent)
        {
            var pending = await _leaveRepo.GetPendingAsync(employee.Id, leaveType.Id, excludeId, ct);
            var held = new Dictionary<int, decimal>();
            foreach (var (year, days) in pending.SelectMany(DaysChargedByYear))
                held[year] = held.GetValueOrDefault(year) + days;

            foreach (var year in daysByYear.Keys)
            {
                var balance = await _balanceRepo.GetByEmployeeAndTypeAsync(employee.Id, leaveType.Id, year, ct);
                if (balance is not null)
                    balances[year] = balance;

                var remaining = balance?.RemainingDays
                    ?? (leaveType.EntitlementKind == LeaveEntitlementKind.YearlyAllowance ? leaveType.MaxDaysPerYear : 0m);
                availableByYear[year] = remaining - held.GetValueOrDefault(year);
            }
        }

        var context = new LeaveRuleContext(
            leaveType, employee, start, end, daysByYear,
            maternityCase, daysAllocatedToFather, approvedEvents, availableByYear);

        LeaveRules.EnsureEligible(context);

        if (await _leaveRepo.HasOverlapAsync(employee.Id, start, end, excludeId, ct))
            throw new DomainException("Employee has an overlapping leave request for these dates.");

        LeaveRules.EnsureWithinLimits(context);

        return new CheckedRequest(
            daysByYear,
            daysByYear.Values.Sum(),
            daysByYear.GetValueOrDefault(start.Year),
            balances);
    }

    /// <summary>A request's days by the year they are charged to: DaysInStartYear to the start year, the rest to the end year.</summary>
    private static IEnumerable<(int Year, decimal Days)> DaysChargedByYear(LeaveRequest r)
    {
        yield return (r.StartDate.Year, r.DaysInStartYear);
        yield return (r.EndDate.Year, r.TotalDays - r.DaysInStartYear);
    }

    public async Task<LeaveRequestDto> RejectAsync(Guid id, Guid rejecterId, RejectLeaveDto dto, CancellationToken ct = default)
    {
        var request = await _leaveRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Leave request {id} not found.");

        if (request.Status != LeaveStatus.Pending)
            throw new DomainException("Only pending leave requests can be rejected.");

        EnsureNotOwnRequest(request, rejecterId, "reject");

        request.Status = LeaveStatus.Rejected;
        request.RejectionReason = dto.RejectionReason;
        request.UpdatedAt = DateTime.UtcNow;

        await _leaveRepo.UpdateAsync(request, ct);
        return ToDto(request);
    }

    /// <summary>
    /// Every approver role could otherwise decide its own leave. <paramref name="deciderId"/> must
    /// come from the caller's employee_id claim, so this cannot be dodged by naming someone else.
    /// An employee withdrawing their own request uses <see cref="CancelAsync"/> instead.
    /// </summary>
    private static void EnsureNotOwnRequest(LeaveRequest request, Guid deciderId, string decision)
    {
        if (request.EmployeeId == deciderId)
            throw new DomainException($"You cannot {decision} your own leave request.");
    }

    public async Task CancelAsync(Guid id, Guid requestingEmployeeId, CancellationToken ct = default)
    {
        var request = await _leaveRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Leave request {id} not found.");

        if (request.EmployeeId != requestingEmployeeId)
            throw new DomainException("You can only cancel your own leave requests.");

        if (request.Status == LeaveStatus.Approved)
        {
            // Give each year back what it was charged. A PerEvent request was never charged and its
            // type has no balance rows, so nothing is found to refund.
            var refunds = DaysChargedByYear(request)
                .GroupBy(x => x.Year)
                .Select(g => (Year: g.Key, Days: g.Sum(x => x.Days)))
                .Where(x => x.Days != 0);

            foreach (var (year, days) in refunds)
            {
                var balance = await _balanceRepo.GetByEmployeeAndTypeAsync(
                    request.EmployeeId, request.LeaveTypeId, year, ct);
                if (balance is null)
                    continue;

                balance.UsedDays -= days;
                balance.UpdatedAt = DateTime.UtcNow;
                await _balanceRepo.UpdateAsync(balance, ct);
            }
        }
        else if (request.Status != LeaveStatus.Pending)
        {
            throw new DomainException("Only pending or approved leave requests can be cancelled.");
        }

        request.Status = LeaveStatus.Cancelled;
        request.UpdatedAt = DateTime.UtcNow;
        await _leaveRepo.UpdateAsync(request, ct);
    }

    private static LeaveRequestDto ToDto(LeaveRequest r) => new(
        r.Id, r.EmployeeId, r.Employee?.FullName ?? string.Empty,
        r.LeaveTypeId, r.LeaveType?.Name ?? string.Empty,
        r.StartDate, r.EndDate, r.TotalDays, r.Reason,
        r.Status, r.ApprovedBy, r.ApprovedAt, r.RejectionReason, r.CreatedAt,
        r.MaternityCase, r.DaysAllocatedToFather,
        r.DocumentStorageKey != null, r.DocumentFileName);
}
