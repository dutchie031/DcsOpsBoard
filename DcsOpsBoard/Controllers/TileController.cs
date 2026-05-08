using System;
using DcsOpsBoard.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DcsOpsBoard.Controllers;

[ApiController]
[Route("api")]
public class TileController : ControllerBase
{
    private readonly string BaseDirectory;

    public TileController(IOptions<TileConfiguration> tileConfiguration)
    {
        BaseDirectory = tileConfiguration.Value.BaseDirectory;
        Console.WriteLine($"TileController initialized with base directory: {BaseDirectory}");
    }

    [HttpGet("tiles/{map}/{z}/{x}/{y}.webp")]
    public IActionResult GetTile(string map, int z, int x, int y)
    {
        this.HttpContext.Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
        if(z < 6 || z > 15)
        {
            return NoContent();
        }

        string filePath = Path.Combine(BaseDirectory, "base", map, z.ToString(), x.ToString(), $"{y}.webp");
        if (!System.IO.File.Exists(filePath))
        {
            return NoContent();
        }
        return PhysicalFile(filePath, "image/webp");

    }

    [HttpGet("detailed-tiles/{map}/{z}/{x}/{y}.webp")]
    public IActionResult GetDetailedTile(string map, int z, int x, int y)
    {
        this.HttpContext.Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
        if(z < 15 || z > 17)
        {
            return NoContent();
        }

        string filePath = Path.Combine(BaseDirectory, "detailed", map, z.ToString(), x.ToString(), $"{y}.webp");
        if (!System.IO.File.Exists(filePath))
        {
            return NoContent();
        }
        return PhysicalFile(filePath, "image/webp");
    }

    [HttpGet("detailed-coverage/{map}/coverage.json")]
    public IActionResult GetDetailedCoverage(string map)
    {
        //Cache control on coverage data should be low, should only really download once per session anyway.

        this.Response.Headers["Cache-Control"] = "public, max-age=3600"; // 1 hour cache for coverage data
        string filePath = Path.Combine(BaseDirectory, "detailed", map, "coverage.json");
        if (!System.IO.File.Exists(filePath))
        {
            return NoContent();
        }
        return PhysicalFile(filePath, "application/json");
    }

    [HttpGet("shaders/{map}/{z}/{x}/{y}.webp")]
    public IActionResult GetShader(string map, int z, int x, int y)
    {
        this.HttpContext.Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
        string filePath = Path.Combine(BaseDirectory, "shaders", map, z.ToString(), x.ToString(), $"{y}.webp");
        if (!System.IO.File.Exists(filePath))
        {
            return NoContent();
        }
        return PhysicalFile(filePath, "image/webp");
    }



}
