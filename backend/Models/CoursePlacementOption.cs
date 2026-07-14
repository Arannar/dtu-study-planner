namespace Planner.Backend.Models;

public sealed class CoursePlacementOption
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public List<string> TimeBlocks { get; init; } = new();
}
