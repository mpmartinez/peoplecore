using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using PeopleCore.API.Accounts;
using PeopleCore.API.Controllers.Account;
using PeopleCore.API.Controllers.Auth;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// A temporary password was chosen by somebody else and passed on by hand. Until the user replaces
/// it, the only thing it opens is the door to replacing it.
/// </summary>
public class PasswordChangeRequiredFilterTests
{
    private static ClaimsPrincipal SignedIn(bool mustChangePassword)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "u1") };
        if (mustChangePassword) claims.Add(new Claim(AccountClaims.MustChangePassword, "true"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }

    private static ActionExecutingContext Request(ClaimsPrincipal user, params object[] endpointMetadata)
    {
        var action = new ActionContext(
            new DefaultHttpContext { User = user },
            new RouteData(),
            new ActionDescriptor { EndpointMetadata = endpointMetadata.ToList() });
        return new ActionExecutingContext(action, new List<IFilterMetadata>(), new Dictionary<string, object?>(), controller: new object());
    }

    [Fact]
    public void OnATemporaryPassword_AnOrdinaryAuthorizedAction_IsRefused()
    {
        var context = Request(SignedIn(mustChangePassword: true), new AuthorizeAttribute());

        new PasswordChangeRequiredFilter().OnActionExecuting(context);

        var refused = context.Result.Should().BeOfType<ObjectResult>().Subject;
        refused.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        refused.Value.Should().BeOfType<ProblemDetails>().Which.Title.Should().Be("Password change required");
    }

    [Fact]
    public void OnATemporaryPassword_AnExemptAction_Runs()
    {
        var context = Request(SignedIn(mustChangePassword: true), new AuthorizeAttribute(), new AllowDuringPasswordChangeAttribute());

        new PasswordChangeRequiredFilter().OnActionExecuting(context);

        context.Result.Should().BeNull();
    }

    [Fact]
    public void OnATemporaryPassword_AnActionWithNoAuthorizeMetadata_Runs()
    {
        // No [Authorize] anywhere means the endpoint is anonymous - refusing it here would block a
        // door the rest of the app never locked in the first place, /login being the case in point.
        var context = Request(SignedIn(mustChangePassword: true));

        new PasswordChangeRequiredFilter().OnActionExecuting(context);

        context.Result.Should().BeNull();
    }

    [Fact]
    public void OnATemporaryPassword_AnAuthorizedActionMarkedAllowAnonymous_Runs()
    {
        var context = Request(SignedIn(mustChangePassword: true), new AuthorizeAttribute(), new AllowAnonymousAttribute());

        new PasswordChangeRequiredFilter().OnActionExecuting(context);

        context.Result.Should().BeNull();
    }

    [Fact]
    public void WithAPasswordOfTheirOwn_EveryActionRuns()
    {
        var context = Request(SignedIn(mustChangePassword: false));

        new PasswordChangeRequiredFilter().OnActionExecuting(context);

        context.Result.Should().BeNull();
    }

    [Theory]
    [InlineData(typeof(AuthController), nameof(AuthController.ChangePassword))]
    [InlineData(typeof(ProfileController), nameof(ProfileController.Get))]
    public void ChangingThePassword_AndReadingYourOwnProfile_AreExempt(Type controller, string action)
    {
        controller.GetMethod(action)!.GetCustomAttribute<AllowDuringPasswordChangeAttribute>().Should().NotBeNull();
    }

    [Fact]
    public void LoggingIn_CarriesNoAuthorizeAttribute_SoTheFilterNeverBlocksIt()
    {
        // Deliberate: /login must stay reachable by a bearer token that still carries
        // must_change_password, or someone who abandons the "set a new password" screen can never
        // sign in again to get back to it.
        typeof(AuthController).GetMethod(nameof(AuthController.Login))!
            .GetCustomAttribute<AuthorizeAttribute>().Should().BeNull();
    }
}
