namespace Planner.Backend.Models;

public sealed class CourseSearchRequest
{
    public string? Query { get; init; }
    public int? Volume { get; init; }
}
