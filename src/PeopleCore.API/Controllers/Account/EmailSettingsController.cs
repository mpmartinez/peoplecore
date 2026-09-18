using System.Net.Mail;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
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

    // A test recipient chosen by the admin, not a signed-in account's own claim: whitespace, angle
    // brackets, commas and semicolons are how a display-name or a second address/header gets in.
    private static readonly char[] UnsafeAddressCharacters = [' ', '\t', '\r', '\n', '<', '>', ',', ';'];

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

    /// <summary>
    /// Sends to the address given, or to the administrator asking - the one address we know is
    /// theirs - when none is given. The admin account's own claim is often not a real mailbox in
    /// production, which is the whole reason to let the caller pick where the test goes.
    /// </summary>
    [HttpPost("test")]
    public async Task<IActionResult> SendTest([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] SendTestEmailRequest? request, CancellationToken ct)
    {
        var to = request?.To?.Trim();
        if (string.IsNullOrEmpty(to))
        {
            to = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(to)) return SettingsProblem("Your account has no email address to send a test to.");
        }
        else if (!IsUsableTestAddress(to))
        {
            return SettingsProblem("Enter a valid email address to send the test to.");
        }

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

    // MailAddress accepts "Display Name <addr>" and folds it down to the bare address; comparing
    // the parsed .Address back against the trimmed input is what rejects that form (and the header
    // tricks that ride on it), rather than trusting whatever MailAddress managed to parse out.
    private static bool IsUsableTestAddress(string to)
    {
        if (to.IndexOfAny(UnsafeAddressCharacters) >= 0) return false;
        try
        {
            return new MailAddress(to).Address == to;
        }
        catch (FormatException)
        {
            return false;
        }
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

public record SendTestEmailRequest(string? To);
