using Microsoft.AspNetCore.Mvc;
using Planner.Backend.Services;

namespace Planner.Backend.Controllers;

[ApiController]
[Route("api/programmes")]
public sealed class ProgrammesController : ControllerBase
{
    private readonly IProgrammeService _programmeService;

    public ProgrammesController(IProgrammeService programmeService)
    {
        _programmeService = programmeService;
    }

    [HttpGet]
    public async Task<IActionResult> GetProgrammes([FromQuery] int volume)
    {
        return Ok(await _programmeService.GetProgrammesAsync(volume));
    }

    [HttpGet("{code}/definition")]
    public async Task<IActionResult> GetProgrammeDefinition(
        [FromRoute] string code,
        [FromQuery] int volume,
        [FromQuery] string language = "da-DK")
    {
        var definition = await _programmeService.GetProgrammeDefinitionAsync(volume, code, language);
        return definition is null ? NotFound() : Ok(definition);
    }
}
