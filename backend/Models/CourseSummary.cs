namespace Planner.Backend.Models;

public sealed class CourseSummary
{
    public string CourseCode { get; init; } = "";
    public string Title { get; init; } = "";
    public string? CourseLevel { get; init; }
    public double? Ects { get; init; }
    public string? ScheduleText { get; init; }
    public string GradingMode { get; init; } = "unknown";
    public string ExaminerMode { get; init; } = "unknown";
    public List<string> TimeBlocks { get; init; } = new();
    public string? SelectedPlacementOptionId { get; init; }
    public List<CoursePlacementOption> PlacementOptions { get; init; } = new();

    public List<string> RawScheduleKeys { get; init; } = new();
    public string Language { get; init; } = "en-GB";
}
