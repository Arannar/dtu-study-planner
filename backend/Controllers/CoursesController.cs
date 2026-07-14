// CoursesController
// → asks for a volume, or resolves a default one
// → calls CourseCatalogService
// → returns normalized JSON

using Microsoft.AspNetCore.Mvc;
using Planner.Backend.Models;
using Planner.Backend.Services;

namespace Planner.Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class CoursesController : ControllerBase
{
    private readonly ICourseCatalogService _catalog;
    private readonly IVolumeResolver _volumeResolver;
    private readonly ILogger<CoursesController> _logger;

    public CoursesController(
        ICourseCatalogService catalog,
        IVolumeResolver volumeResolver,
        ILogger<CoursesController> logger)
    {
        _catalog = catalog;
        _volumeResolver = volumeResolver;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int? volume = null, [FromQuery] string? codes = null)
    {
        var effectiveVolume = await _volumeResolver.ResolveAsync(volume);

        var parsedCodes = string.IsNullOrWhiteSpace(codes)
            ? Array.Empty<string>()
            : codes
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .ToArray();

        _logger.LogInformation(
            "CoursesController using volume={Volume}, codeCount={Count}, codes={Codes}",
            effectiveVolume,
            parsedCodes.Length,
            string.Join(",", parsedCodes));

        CoursesResponse result = await _catalog.GetCoursesForStudyPlanAsync(effectiveVolume, parsedCodes);

        return Ok(result);
    }
    
}
