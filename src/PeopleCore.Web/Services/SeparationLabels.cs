namespace PeopleCore.Web.Services;

/// <summary>
/// Readable words for the separation enums, kept in one place so the list, the detail page and
/// the record form agree with each other. A dropdown's option value stays the enum name -
/// RecordSeparationRequest sends that on the wire - only the text next to it comes from here.
/// </summary>
public static class SeparationLabels
{
    public static string TypeOf(SeparationType type) => type switch
    {
        SeparationType.Resignation => "Resignation",
        SeparationType.TerminationJustCause => "Termination for just cause",
        SeparationType.AuthorizedCause => "Authorized cause",
        SeparationType.EndOfContract => "End of contract",
        SeparationType.Retirement => "Retirement",
        SeparationType.Death => "Death",
        _ => type.ToString()
    };

    public static string CauseOf(AuthorizedCause cause) => cause switch
    {
        AuthorizedCause.Redundancy => "Redundancy",
        AuthorizedCause.Retrenchment => "Retrenchment",
        AuthorizedCause.ClosureNotDueToLosses => "Closure (not due to losses)",
        AuthorizedCause.ClosureDueToSeriousLosses => "Closure due to serious losses",
        AuthorizedCause.LaborSavingDevices => "Labor-saving devices",
        AuthorizedCause.Disease => "Disease",
        _ => cause.ToString()
    };

    public static string StatusOf(SeparationStatus status) => status switch
    {
        SeparationStatus.NoticeGiven => "Notice given",
        SeparationStatus.Separated => "Separated",
        _ => status.ToString()
    };

    /// <summary>The type label, plus ": &lt;cause&gt;" when the type is AuthorizedCause and a cause is given.</summary>
    public static string TypeWithCause(SeparationType type, AuthorizedCause? cause) =>
        type == SeparationType.AuthorizedCause && cause is { } c ? $"{TypeOf(type)}: {CauseOf(c)}" : TypeOf(type);
}
