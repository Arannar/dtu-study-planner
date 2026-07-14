namespace Planner.Backend.Models;

public sealed class PlacementRequest
{
    public required StudyPlan Plan { get; init; }
    public required PlannedCourse Candidate { get; init; }
    public string? ProgrammeLevel { get; init; }
    public List<string> ApprovedMscElectiveCourseCodes { get; init; } = [];
    public List<string> MandatoryCourseCodes { get; init; } = [];
    public ProgrammeBucketLimits? BucketLimits { get; init; }
}

public sealed class ValidateSemesterRequest
{
    public required StudyPlan Plan { get; init; }
    public int Semester { get; init; }
}

public sealed class ValidatePlanRequest
{
    public required StudyPlan Plan { get; init; }
    public string? ProgrammeLevel { get; init; }
    public List<string> MandatoryCourseCodes { get; init; } = [];
    public ProgrammeBucketLimits? BucketLimits { get; init; }
}
