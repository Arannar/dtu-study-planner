namespace Planner.Backend.Models;

public sealed class PlacementResult
{
    public bool Allowed { get; init; }
    public string Message { get; init; } = "";
    public List<string> ConflictingCourseCodes { get; init; } = new();
    public List<string> SharedTimeBlocks { get; init; } = new();
}