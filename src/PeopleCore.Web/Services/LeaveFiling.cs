namespace PeopleCore.Web.Services;

/// <summary>
/// What My Leave needs to know about filing before the API says so: the maternity limit shown as
/// the employee fills the form in, and the supporting-document checks made before an upload.
/// </summary>
/// <remarks>
/// The Web project can't reference the Domain, so these are copies. The maternity numbers are
/// <c>PeopleCore.Domain.Leave.StatutoryLeave</c>'s (RA 11210); the document rules are
/// <c>PeopleCore.Application.Leave.Services.LeaveDocumentService</c>'s. Change them together.
/// </remarks>
public static class LeaveFiling
{
    /// <summary>StatutoryLeave.MaternitySoloParentExtraDays: added to a live birth for a qualified solo parent.</summary>
    public const decimal MaternitySoloParentExtraDays = 15m;

    /// <summary>StatutoryLeave.MaternityMiscarriageDays: the whole leave for a miscarriage or emergency termination.</summary>
    public const decimal MaternityMiscarriageDays = 60m;

    /// <summary>StatutoryLeave.MaxDaysAllocatedToFather.</summary>
    public const int MaxDaysAllocatedToFather = 7;

    /// <summary>The API's own refusal (LeaveRules) for father days out of range.</summary>
    public const string FatherDaysRefusal = "Up to 7 days can be allocated to the father, for a live birth only.";

    /// <summary>LeaveDocumentService.MaxSizeBytes.</summary>
    public const long MaxDocumentBytes = 10 * 1024 * 1024;

    /// <summary>The API's own refusal (LeaveDocumentService.FileRefusal) for a file it won't store.</summary>
    public const string FileRefusal = "Attach a PDF, JPG or PNG of at most 10 MB.";

    /// <summary>For the file input's <c>accept</c>.</summary>
    public const string AcceptedExtensions = ".pdf,.jpg,.jpeg,.png";

    /// <summary>
    /// The days a maternity request may take: the type's live-birth days, 15 more for a qualified
    /// solo parent, less the days given to the father; or 60 for a miscarriage.
    /// </summary>
    public static decimal MaternityLimit(decimal liveBirthDays, bool hasSoloParentBonus, MaternityCase maternityCase, int daysAllocatedToFather) =>
        maternityCase == MaternityCase.MiscarriageOrEmergencyTermination
            ? MaternityMiscarriageDays
            : liveBirthDays + (hasSoloParentBonus ? MaternitySoloParentExtraDays : 0m) - daysAllocatedToFather;

    /// <summary>
    /// The content type to upload a file as, from its extension; null when the API wouldn't accept
    /// it (the wrong type, empty, or over 10 MB). The extension decides rather than the browser's
    /// type, which some browsers leave blank.
    /// </summary>
    public static string? DocumentContentType(string fileName, long size)
    {
        if (size <= 0 || size > MaxDocumentBytes) return null;
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            _ => null,
        };
    }
}
