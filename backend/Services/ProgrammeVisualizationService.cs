namespace Planner.Backend.Services;

public interface IProgrammeVisualizationService
{
    Task<ProgramVisualizationMap> GetProgramVisualizationMapAsync(int educationId, string code);
}

public sealed class ProgrammeVisualizationService(IDtuGateway gateway, ILogger<ProgrammeVisualizationService> logger) : IProgrammeVisualizationService
{
    public async Task<ProgramVisualizationMap> GetProgramVisualizationMapAsync(int educationId, string code)
    {
        var views = (await gateway.GetVisualizationsAsync(educationId) ?? [])
            .Select(view => new VisualizationItem
            {
                Id = view.Id, Guid = view.Guid, NameDanish = view.Name?.Danish, NameEnglish = view.Name?.English
            }).OrderBy(view => view.NameDanish ?? view.NameEnglish).ToList();
        var map = new ProgramVisualizationMap
        {
            CoreViews = views.Where(view => ProgrammeRules.CoreVisualizationNames.Contains(view.NameDanish ?? view.NameEnglish ?? "", StringComparer.OrdinalIgnoreCase)).ToList(),
            RecommendedStudyPackageViews = views.Where(view => ProgrammeRules.IsRecommendedView(code, view.NameDanish ?? view.NameEnglish ?? "")).ToList()
        };
        logger.LogInformation("Mapped programme views education={EducationId} code={Code} core={CoreCount} packages={PackageCount}", educationId, code, map.CoreViews.Count, map.RecommendedStudyPackageViews.Count);
        return map;
    }
}

public sealed class ProgramVisualizationMap
{
    public List<VisualizationItem> CoreViews { get; init; } = [];
    public List<VisualizationItem> RecommendedStudyPackageViews { get; init; } = [];
}

public sealed class VisualizationItem
{
    public int Id { get; init; }
    public Guid Guid { get; init; }
    public string? NameDanish { get; init; }
    public string? NameEnglish { get; init; }
}
