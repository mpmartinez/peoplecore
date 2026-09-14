using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using PeopleCore.API.Controllers.Auth;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// Every endpoint says out loud who may call it. Authorization here is opt-in per controller, so an
/// action that declares neither [Authorize] nor [AllowAnonymous] is open to the internet by
/// omission - and PasswordChangeRequiredFilter refuses it to anyone on a temporary password, which
/// would break a genuinely public endpoint in a way no other test would notice.
/// </summary>
public class ControllerAuthorizationCoverageTests
{
    [Fact]
    public void EveryControllerAction_DeclaresAuthorizeOrAllowAnonymous()
    {
        var undeclared =
            from controller in typeof(AuthController).Assembly.GetTypes()
            where typeof(ControllerBase).IsAssignableFrom(controller) && !controller.IsAbstract
            from action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            where action.GetCustomAttributes<HttpMethodAttribute>().Any()
            where !Declares(action) && !Declares(controller)
            select $"{controller.Name}.{action.Name}";

        undeclared.ToList().Should().BeEmpty("every endpoint must carry [Authorize] or [AllowAnonymous] on the action or its controller");
    }

    private static bool Declares(MemberInfo member) =>
        member.GetCustomAttributes(inherit: true).Any(a => a is IAuthorizeData or IAllowAnonymous);
}
