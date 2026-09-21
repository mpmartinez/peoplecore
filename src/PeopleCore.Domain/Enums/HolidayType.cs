namespace PeopleCore.Domain.Enums;

/// <summary>
/// How a date on the holiday calendar is paid. <see cref="SpecialWorking"/> is proclaimed but is
/// an ordinary working day for pay: no premium, and an unworked one is an absence.
/// </summary>
public enum HolidayType { RegularHoliday, SpecialNonWorking, SpecialWorking }
