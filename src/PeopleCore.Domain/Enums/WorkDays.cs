namespace PeopleCore.Domain.Enums;

/// <summary>
/// The days of the week a shift template schedules as working days.
/// <para>
/// A shift template's work week decides which dates it schedules at all. Payroll derives an
/// absence only for a date that is a scheduled working day - never for a rest day, and never for
/// a date with no schedule to begin with. Getting the work week wrong therefore has a direct wage
/// consequence: a template whose <see cref="WorkDays"/> under-states the real work week (e.g. the
/// fixed Monday-to-Friday assumption applied to a six-day employee) deducts an absence for a day
/// the employee was never expected to work.
/// </para>
/// </summary>
[Flags]
public enum WorkDays
{
    None      = 0,
    Monday    = 1 << 0,
    Tuesday   = 1 << 1,
    Wednesday = 1 << 2,
    Thursday  = 1 << 3,
    Friday    = 1 << 4,
    Saturday  = 1 << 5,
    Sunday    = 1 << 6,

    MondayToFriday   = Monday | Tuesday | Wednesday | Thursday | Friday,
    MondayToSaturday = MondayToFriday | Saturday,
    AllDays          = MondayToSaturday | Sunday
}

/// <summary>
/// Maps <see cref="System.DayOfWeek"/> onto <see cref="WorkDays"/>. The two do not line up bit
/// for bit - <see cref="System.DayOfWeek.Sunday"/> is 0, while <see cref="WorkDays"/> puts Monday
/// at the lowest bit so that the common Monday-to-Friday and Monday-to-Saturday work weeks are
/// contiguous bit ranges - so a translation is needed rather than a direct cast.
/// </summary>
public static class WorkDaysExtensions
{
    /// <summary>Returns the single <see cref="WorkDays"/> flag corresponding to <paramref name="dayOfWeek"/>.</summary>
    public static WorkDays ToWorkDays(this DayOfWeek dayOfWeek) => dayOfWeek switch
    {
        DayOfWeek.Monday => WorkDays.Monday,
        DayOfWeek.Tuesday => WorkDays.Tuesday,
        DayOfWeek.Wednesday => WorkDays.Wednesday,
        DayOfWeek.Thursday => WorkDays.Thursday,
        DayOfWeek.Friday => WorkDays.Friday,
        DayOfWeek.Saturday => WorkDays.Saturday,
        DayOfWeek.Sunday => WorkDays.Sunday,
        _ => throw new ArgumentOutOfRangeException(nameof(dayOfWeek), dayOfWeek, "Unrecognized DayOfWeek value.")
    };

    /// <summary>Whether <paramref name="workDays"/> schedules <paramref name="dayOfWeek"/> as a working day.</summary>
    public static bool IncludesDay(this WorkDays workDays, DayOfWeek dayOfWeek) =>
        (workDays & dayOfWeek.ToWorkDays()) != 0;
}
