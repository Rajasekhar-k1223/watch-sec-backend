using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using watch_sec_backend.Services;

namespace watch_sec_backend.Controllers;

[ApiController]
[Route("api/seed")]
public class SeedingController : ControllerBase
{
    private readonly DataSeeder _seeder;

    public SeedingController(DataSeeder seeder)
    {
        _seeder = seeder;
    }

    [HttpPost("full")]
    public async Task<IActionResult> SeedFullData()
    {
        await _seeder.SeedAsync();
        return Ok("Mock Data Checks/Generation Completed.");
    }
}
