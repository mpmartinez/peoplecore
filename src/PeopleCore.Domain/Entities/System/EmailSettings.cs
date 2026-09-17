namespace PeopleCore.Domain.Entities.System;

/// <summary>
/// How the application sends mail. Exactly one row, so the settings page edits a thing that always
/// exists rather than a list of one. The password is stored encrypted; see EmailSettingsStore.
/// </summary>
public class EmailSettings
{
    public const int SingleRowId = 1;

    public int Id { get; set; } = SingleRowId;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public string? Username { get; set; }
    public string? PasswordProtected { get; set; }
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "PeopleCore";

    /// <summary>Where reset links point. The web app's address, not the API's.</summary>
    public string AppBaseUrl { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; }
}
