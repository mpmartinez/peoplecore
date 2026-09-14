using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PeopleCore.API.Controllers.Account;
using PeopleCore.Infrastructure.Identity.UserAccounts;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// The HTTP face of account administration: Admin only, the acting account always the one the token
/// names, and every refusal carried to the client as a problem detail it can show as it is. The rules
/// themselves are proven against Postgres in UserAccountServiceTests.
/// </summary>
public class UsersControllerTests
{
    private const string CallerId = "3f2b8c1e-7a4d-4e9b-9c6f-1d2e3f4a5b6c";
    private const string TargetId = "7d1e5a2c-9b3f-4c8d-8a6e-2f4b6d8c0e1a";

    private static readonly UserAccountDto Ana =
        new(TargetId, "ana@company.test", "Ana", "Reyes", ["Employee"], null, null, null);

    private readonly Mock<IUserAccountService> _accounts = new();
    private readonly UsersController _sut;

    public UsersControllerTests()
    {
        _sut = new UsersController(_accounts.Object);
        SignInAs(CallerId);
    }

    private void SignInAs(string? userId)
    {
        var claims = userId is null ? [] : new[] { new Claim(ClaimTypes.NameIdentifier, userId) };
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")) }
        };
    }

    private static UpdateUserAccountRequest AnUpdate() => new("ana@company.test", "Ana", "Reyes", ["Employee"], null, null);

    private static ProblemDetails ProblemOf(IActionResult? result, int status)
    {
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(status);
        return objectResult.Value.Should().BeOfType<ProblemDetails>().Subject;
    }

    [Fact]
    public void OnlyAdmins_MayManageAccounts()
    {
        var authorize = typeof(UsersController).GetCustomAttribute<AuthorizeAttribute>();

        authorize.Should().NotBeNull();
        authorize!.Roles.Should().Be("Admin");
        typeof(UsersController).GetMethods()
            .Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>() is not null || m.GetCustomAttribute<AuthorizeAttribute>() is not null)
            .Should().BeEmpty("no action may loosen or override the class-level Admin rule");
    }

    [Fact]
    public async Task Listing_PassesTheSearchAndPageThrough()
    {
        var page = PeopleCore.Application.Common.DTOs.PagedResult<UserAccountDto>.Create([Ana], 1, 2, 10);
        _accounts.Setup(a => a.ListAsync("ana", 2, 10, It.IsAny<CancellationToken>())).ReturnsAsync(page);

        var result = await _sut.GetAll("ana", 2, 10);

        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(page);
    }

    [Fact]
    public async Task AnAccountThatDoesNotExist_IsNotFound()
    {
        (await _sut.GetById("missing", CancellationToken.None)).Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Creating_Answers201_PointingAtTheNewAccount()
    {
        var request = new CreateUserAccountRequest("ana@company.test", "Passw0rd!", "Ana", "Reyes", ["Employee"], null);
        _accounts.Setup(a => a.CreateAsync(request, It.IsAny<CancellationToken>())).ReturnsAsync(UserAccountResult.Ok(Ana));

        var result = await _sut.Create(request, CancellationToken.None);

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.ActionName.Should().Be(nameof(UsersController.GetById));
        created.RouteValues!["id"].Should().Be(TargetId);
        created.Value.Should().Be(Ana);
    }

    [Theory]
    [InlineData(UserAccountFailure.Invalid, 400)]
    [InlineData(UserAccountFailure.Conflict, 409)]
    public async Task ARefusedCreate_CarriesTheReason(UserAccountFailure failure, int status)
    {
        _accounts.Setup(a => a.CreateAsync(It.IsAny<CreateUserAccountRequest>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new UserAccountResult(null, failure, "An account with the email ana@company.test already exists."));

        var result = await _sut.Create(new CreateUserAccountRequest(null, null, null, null, null, null), CancellationToken.None);

        ProblemOf(result.Result, status).Detail.Should().Be("An account with the email ana@company.test already exists.");
    }

    [Fact]
    public async Task Updating_ActsAsTheTokensAccount()
    {
        var request = AnUpdate();
        _accounts.Setup(a => a.UpdateAsync(TargetId, request, CallerId, It.IsAny<CancellationToken>())).ReturnsAsync(UserAccountResult.Ok(Ana));

        var result = await _sut.Update(TargetId, request, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(Ana);
    }

    [Theory]
    [InlineData(UserAccountFailure.NotFound, 404)]
    [InlineData(UserAccountFailure.Invalid, 400)]
    [InlineData(UserAccountFailure.Conflict, 409)]
    public async Task ARefusedUpdate_CarriesTheReason(UserAccountFailure failure, int status)
    {
        _accounts.Setup(a => a.UpdateAsync(TargetId, It.IsAny<UpdateUserAccountRequest>(), CallerId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new UserAccountResult(null, failure, "Nope."));

        var result = await _sut.Update(TargetId, AnUpdate(), CancellationToken.None);

        ProblemOf(result.Result, status).Detail.Should().Be("Nope.");
    }

    [Fact]
    public async Task Deleting_ActsAsTheTokensAccount_AndAnswers204()
    {
        _accounts.Setup(a => a.DeleteAsync(TargetId, CallerId, It.IsAny<CancellationToken>())).ReturnsAsync(UserAccountResult.Ok(null));

        (await _sut.Delete(TargetId, CancellationToken.None)).Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task ARefusedDelete_CarriesTheReason()
    {
        _accounts.Setup(a => a.DeleteAsync(CallerId, CallerId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(UserAccountResult.Invalid("You cannot delete the account you are signed in with."));

        var result = await _sut.Delete(CallerId, CancellationToken.None);

        ProblemOf(result, 400).Detail.Should().Be("You cannot delete the account you are signed in with.");
    }

    [Fact]
    public async Task ATokenWithoutAnAccountId_ChangesNothing()
    {
        SignInAs(null);

        (await _sut.Update(TargetId, AnUpdate(), CancellationToken.None)).Result.Should().BeOfType<UnauthorizedResult>();
        (await _sut.Delete(TargetId, CancellationToken.None)).Should().BeOfType<UnauthorizedResult>();
        _accounts.VerifyNoOtherCalls();
    }
}
