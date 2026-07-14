namespace Planner.Backend.Models;

public sealed class SemesterValidationResult
{
    public int Semester { get; init; }
    public bool Allowed { get; init; }
    public double TotalEcts { get; init; }
    public List<PlacementResult> Conflicts { get; init; } = new();
}

public sealed class PlanValidationResult
{
    public bool Allowed { get; init; }
    public List<SemesterValidationResult> Semesters { get; init; } = new();
    public List<PlacementResult> PlanConflicts { get; init; } = new();
}
