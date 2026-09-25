namespace PeopleCore.Domain.Leave;

/// <summary>RA 11210 (Expanded Maternity Leave Law) constants.</summary>
public static class StatutoryLeave
{
    /// <summary>Base maternity leave days for a live birth.</summary>
    public const decimal MaternityLiveBirthDays = 105m;

    /// <summary>Additional days for a qualified solo parent, live birth only.</summary>
    public const decimal MaternitySoloParentExtraDays = 15m;

    /// <summary>Maternity leave days for a miscarriage or emergency termination of pregnancy.</summary>
    public const decimal MaternityMiscarriageDays = 60m;

    /// <summary>Most days of the mother's live-birth maternity leave that may be allocated to the father.</summary>
    public const int MaxDaysAllocatedToFather = 7;
}
