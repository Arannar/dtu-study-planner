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

    [HttpGet("{code}/study-flows/{optionId}")]
    public async Task<IActionResult> GetStudyFlow(string code, string optionId, [FromQuery] int volume,
        [FromQuery] string language = "da-DK")
    {
        var option = await _programmeService.GetStudyFlowAsync(volume, code, optionId, language);
        return option is null
            ? Problem(statusCode: 404, title: "Study flow unavailable",
                detail: "This option has no available semester placements for the selected programme and year. Choose another package or the generic plan.")
            : Ok(option);
    }
}
