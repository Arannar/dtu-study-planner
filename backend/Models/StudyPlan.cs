namespace Planner.Backend.Models;

public sealed class StudyPlan
{
    public List<PlannedCourse> Courses { get; init; } = new();
}