namespace Planner.Backend.Models;

public sealed class PlannedCourse
{
    public string CourseCode { get; init; } = "";
    public string Title { get; init; } = "";
    public string? CourseLevel { get; init; }
    public double? Ects { get; init; }
    public int Semester { get; init; }
    public string? PlacementOptionId { get; init; }
    public string? PlacementOptionLabel { get; init; }
    public string? GradingMode { get; init; }
    public string? ExaminerMode { get; init; }
    public List<string> TimeBlocks { get; init; } = new();
    public string? Kind { get; init; }
    public string? ActivityType { get; init; }
    public string? DisplayCode { get; init; }
    public string? ScheduleMode { get; init; }
    public string? Bucket { get; init; }
}
