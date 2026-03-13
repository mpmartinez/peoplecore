using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.API.Controllers.Organization;

[ApiController]
[Route("api/companies")]
[Authorize]
public class CompaniesController : ControllerBase
{
    private readonly AppDbContext _db;
    public CompaniesController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => Ok(await _db.Companies.Select(c => new { c.Id, c.Name }).ToListAsync(ct));
}
