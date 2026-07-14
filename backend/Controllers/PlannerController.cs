using Microsoft.AspNetCore.Mvc;
using Planner.Backend.Models;
using Planner.Backend.Services;

namespace Planner.Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class PlannerController : ControllerBase
{
    private readonly IStudyPlanValidator _validator;

    public PlannerController(IStudyPlanValidator validator)
    {
        _validator = validator;
    }

    [HttpPost("validate-placement")]
    public IActionResult ValidatePlacement([FromBody] PlacementRequest request)
    {
        var result = _validator.ValidatePlacement(
            request.Plan,
            request.Candidate,
            request.ProgrammeLevel,
            request.ApprovedMscElectiveCourseCodes,
            request.MandatoryCourseCodes,
            request.BucketLimits);
        return Ok(result);
    }

    [HttpPost("validate-semester")]
    public IActionResult ValidateSemester([FromBody] ValidateSemesterRequest request)
    {
        var result = _validator.ValidateSemester(request.Plan, request.Semester);
        return Ok(result);
    }

    [HttpPost("validate-plan")]
    public IActionResult ValidatePlan([FromBody] ValidatePlanRequest request)
    {
        var result = _validator.ValidatePlan(
            request.Plan,
            request.ProgrammeLevel,
            request.MandatoryCourseCodes,
            request.BucketLimits);
        return Ok(result);
    }
}
