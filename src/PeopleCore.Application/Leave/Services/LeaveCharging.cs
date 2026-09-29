using PeopleCore.Domain.Entities.Leave;

namespace PeopleCore.Application.Leave.Services;

/// <summary>How a leave request's days are charged against a balance year.</summary>
public static class LeaveCharging
{
    /// <summary>A request's days by the year they are charged to: DaysInStartYear to the start year, the rest to the end year.</summary>
    public static IEnumerable<(int Year, decimal Days)> DaysChargedByYear(LeaveRequest r)
    {
        yield return (r.StartDate.Year, r.DaysInStartYear);
        yield return (r.EndDate.Year, r.TotalDays - r.DaysInStartYear);
    }
}
