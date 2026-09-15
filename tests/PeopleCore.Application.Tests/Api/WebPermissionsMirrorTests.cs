using System.Text.RegularExpressions;
using FluentAssertions;
using PeopleCore.Application.Common.Authorization;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// The Blazor client cannot reference the Application project, so it keeps its own copy of the
/// permission keys. A key missing or misspelt there would silently hide a menu or lock a page.
/// </summary>
public class WebPermissionsMirrorTests
{
    [Fact]
    public void TheWebClientsPermissionKeys_AreTheApisExactly()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "PeopleCore.slnx")))
            root = root.Parent;
        root.Should().NotBeNull("the tests run from inside the repository");

        var source = File.ReadAllText(Path.Combine(root!.FullName, "src", "PeopleCore.Web", "Auth", "Permissions.cs"));
        var webKeys = Regex.Matches(source, @"public const string \w+ = ""([a-z]+\.[a-z-]+)"";")
            .Select(m => m.Groups[1].Value)
            .ToList();

        webKeys.Should().Equal(Permissions.AllKeys);
    }
}
