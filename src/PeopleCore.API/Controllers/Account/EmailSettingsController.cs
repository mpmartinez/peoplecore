using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.API.Authorization;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Infrastructure.Email;

namespace PeopleCore.API.Controllers.Account;

/// <summary>
/// The account the application sends mail from. Behind settings.manage, so mail can be configured
/// by someone who manages neither users nor roles. The stored password is never read back out.
/// </summary>
[ApiController]
[Route("api/email-settings")]
[RequirePermission(Permissions.SettingsManage)]
public class EmailSettingsController : ControllerBase
{
    // AppBaseUrl is interpolated, unencoded, into an HTML href in the reset email. A value carrying
    // any of these breaks out of the attribute or the tag, so it is refused here rather than trusted.
    private static readonly char[] UnsafeUrlCharacters = ['"', '\'', '<', '>', ' ', '\t', '\r', '\n'];

    private readonly IEmailSettingsStore _store;
    private readonly IEmailSender _email;

    public EmailSettingsController(IEmailSettingsStore store, IEmailSender email)
    {
        _store = store;
        _email = email;
    }

    [HttpGet]
    public async Task<ActionResult<EmailSettingsDto>> Get(CancellationToken ct) =>
        await _store.GetViewAsync(ct) is { } view ? Ok(ToDto(view)) : NoContent();

    [HttpPut]
    public async Task<ActionResult<EmailSettingsDto>> Save([FromBody] SaveEmailSettingsRequest request, CancellationToken ct)
    {
        if (Validate(request) is { } problem) return SettingsProblem(problem);

        // An empty password means "keep the one already stored": the page never receives it, so it
        // cannot send it back.
        var password = string.IsNullOrEmpty(request.Password) ? null : request.Password;

        await _store.SaveAsync(new MailAccount(
            request.Host.Trim(), request.Port, request.UseStartTls, request.Username?.Trim(), password,
            request.FromAddress.Trim(), request.FromName.Trim(), request.AppBaseUrl.Trim()), ct);

        var saved = await _store.GetViewAsync(ct);
        return Ok(ToDto(saved!));
    }

    /// <summary>Sends to the administrator asking, which is the one address we know is theirs.</summary>
    [HttpPost("test")]
    public async Task<IActionResult> SendTest(CancellationToken ct)
    {
        var to = User.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrEmpty(to)) return SettingsProblem("Your account has no email address to send a test to.");

        try
        {
            await _email.SendAsync(new EmailMessage(to, string.Empty, "PeopleCore test message",
                "<p>PeopleCore can send mail. Nothing else to do.</p>",
                "PeopleCore can send mail. Nothing else to do."), ct);
        }
        catch (Exception ex)
        {
            // The real reason, on purpose: this is the person who can fix it. MailKit's exception
            // messages describe the SMTP failure, not the credentials that produced it.
            return SettingsProblem($"The mail server refused the message: {ex.Message}");
        }

        return Ok(new { sent = true, to });
    }

    private static string? Validate(SaveEmailSettingsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Host)) return "Enter the mail server's address.";
        if (request.Port is < 1 or > 65535) return "Enter a port between 1 and 65535.";
        if (string.IsNullOrWhiteSpace(request.FromAddress) || !request.FromAddress.Contains('@'))
            return "Enter the address mail is sent from.";
        if (string.IsNullOrWhiteSpace(request.FromName)) return "Enter the name mail is sent from.";
        if (!IsUsableAppBaseUrl(request.AppBaseUrl)) return "Enter the app's web address, starting with https://.";
        return null;
    }

    private static bool IsUsableAppBaseUrl(string? appBaseUrl)
    {
        var trimmed = appBaseUrl?.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var url)) return false;
        if (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp) return false;
        return trimmed!.IndexOfAny(UnsafeUrlCharacters) < 0;
    }

    private static EmailSettingsDto ToDto(MailAccountView view) =>
        new(view.Host, view.Port, view.UseStartTls, view.Username, view.HasPassword,
            view.FromAddress, view.FromName, view.AppBaseUrl, view.UpdatedAt);

    private BadRequestObjectResult SettingsProblem(string detail) =>
        BadRequest(new ProblemDetails { Title = "Email settings not saved", Detail = detail, Status = StatusCodes.Status400BadRequest });
}

public record EmailSettingsDto(string Host, int Port, bool UseStartTls, string? Username, bool HasPassword,
    string FromAddress, string FromName, string AppBaseUrl, DateTime? UpdatedAt);

public record SaveEmailSettingsRequest(string Host, int Port, bool UseStartTls, string? Username, string? Password,
    string FromAddress, string FromName, string AppBaseUrl);
