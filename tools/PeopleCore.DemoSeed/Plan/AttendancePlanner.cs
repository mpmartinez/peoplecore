using System.Globalization;
using System.Text;

namespace PeopleCore.DemoSeed.Plan;

public record AttendanceRow(string EmployeeNumber, DateOnly Date, TimeOnly TimeIn, TimeOnly TimeOut);

/// <summary>
/// One month of clock-ins in the shape api/attendance/import reads. About one day in fifty is an
/// absence, one in ten a late arrival, one in thirty an early exit. A day of approved leave has no
/// row. An overtime day clocks out after the overtime ends. Each month has its own random stream,
/// so building a month never depends on having built another.
/// </summary>
public static class AttendancePlanner
{
    public static IReadOnlyList<AttendanceRow> ForMonth(
        IReadOnlyList<Person> people, int month, DateOnly today,
        IReadOnlyList<LeaveFiling> leave, IReadOnlyList<OvertimeFiling> overtime, int seed)
    {
        var rng = new Random(unchecked(seed * 100 + month));
        var first = new DateOnly(2026, month, 1);
        var monthEnd = first.AddMonths(1).AddDays(-1);
        var last = monthEnd < today ? monthEnd : today.AddDays(-1);

        var onLeave = leave.Where(l => l.Decision == Decision.Approved)
            .SelectMany(l => Calendar.WorkDays(l.Start, l.End).Select(d => (l.PersonNumber, d)))
            .ToHashSet();
        var overtimeEnds = overtime.ToDictionary(o => (o.PersonNumber, o.Date), o => o.End);
        var rows = new List<AttendanceRow>();

        foreach (var person in people)
        {
            var from = person.ActiveFrom > first ? person.ActiveFrom : first;
            foreach (var day in Calendar.WorkDays(from, last))
            {
                var roll = rng.NextDouble();
                if (onLeave.Contains((person.Number, day))) continue;

                var worksLate = overtimeEnds.TryGetValue((person.Number, day), out var overtimeEnd);
                if (!worksLate && roll < 0.02) continue;

                var timeIn = roll < 0.12
                    ? new TimeOnly(8, 5).AddMinutes(rng.Next(0, 26))
                    : new TimeOnly(7, 40).AddMinutes(rng.Next(0, 21));
                var timeOut = worksLate
                    ? overtimeEnd.AddMinutes(rng.Next(0, 11))
                    : rng.NextDouble() < 0.03
                        ? new TimeOnly(16, 0).AddMinutes(rng.Next(0, 46))
                        : new TimeOnly(17, 0).AddMinutes(rng.Next(0, 26));

                rows.Add(new AttendanceRow(person.EmployeeNumber, day, timeIn, timeOut));
            }
        }

        return rows;
    }

    public static string ToCsv(IEnumerable<AttendanceRow> rows)
    {
        var csv = new StringBuilder("employee_number,date,time_in,time_out\n");
        foreach (var row in rows)
            csv.Append(row.EmployeeNumber).Append(',')
               .Append(row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
               .Append(row.TimeIn.ToString("HH:mm", CultureInfo.InvariantCulture)).Append(',')
               .Append(row.TimeOut.ToString("HH:mm", CultureInfo.InvariantCulture)).Append('\n');
        return csv.ToString();
    }
}
