namespace Planner.Backend.Models;

public sealed class CoursesResponse
{
    public List<CourseSummary> Courses { get; init; } = new();
    public List<string> MissingCourseCodes { get; init; } = new();
}
