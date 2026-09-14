using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Application.Common.DTOs;
using PeopleCore.Infrastructure.Identity.UserAccounts;

namespace PeopleCore.API.Controllers.Account;

/// <summary>
/// Sign-in accounts: who can log in, with which roles, as which employee. Admin only - an account
/// that can grant roles can grant itself any of them, so HRManager is deliberately not let in.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly IUserAccountService _accounts;

    public UsersController(IUserAccountService accounts) => _accounts = accounts;

    [HttpGet]
    public async Task<ActionResult<PagedResult<UserAccountDto>>> GetAll(
        [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await _accounts.ListAsync(search, page, pageSize, ct));

    [HttpGet("roles")]
    public async Task<ActionResult<IReadOnlyList<string>>> GetRoles(CancellationToken ct)
        => Ok(await _accounts.GetRoleNamesAsync(ct));

    [HttpGet("{id}")]
    public async Task<ActionResult<UserAccountDto>> GetById(string id, CancellationToken ct)
    {
        var user = await _accounts.GetAsync(id, ct);
        return user is null ? NotFound() : Ok(user);
    }

    [HttpPost]
    public async Task<ActionResult<UserAccountDto>> Create([FromBody] CreateUserAccountRequest request, CancellationToken ct)
    {
        var result = await _accounts.CreateAsync(request, ct);
        if (!result.Succeeded) return Problem(result, "Account not created");

        return CreatedAtAction(nameof(GetById), new { id = result.User!.Id }, result.User);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<UserAccountDto>> Update(string id, [FromBody] UpdateUserAccountRequest request, CancellationToken ct)
    {
        if (CallerId is not { } callerId) return Unauthorized();

        var result = await _accounts.UpdateAsync(id, request, callerId, ct);
        return result.Succeeded ? Ok(result.User) : Problem(result, "Account not saved");
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        if (CallerId is not { } callerId) return Unauthorized();

        var result = await _accounts.DeleteAsync(id, callerId, ct);
        return result.Succeeded ? NoContent() : Problem(result, "Account not deleted");
    }

    private string? CallerId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    private ObjectResult Problem(UserAccountResult result, string title)
    {
        var status = result.Failure switch
        {
            UserAccountFailure.NotFound => StatusCodes.Status404NotFound,
            UserAccountFailure.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };

        return StatusCode(status, new ProblemDetails { Title = title, Detail = result.Message, Status = status });
    }
}
