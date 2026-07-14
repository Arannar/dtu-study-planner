using Planner.Backend.Soap.Visualizations;

namespace Planner.Backend.Services;

public interface IProgrammeVisualizationService
{
    Task<ProgramVisualizationMap> GetProgramVisualizationMapAsync(
        int volume,
        int educationId,
        string code,
        string language);
}

public sealed class ProgrammeVisualizationService : IProgrammeVisualizationService
{
    private const string ElectricalEngineeringCode = "ELEKTEK23";

    private readonly VisualizationServiceClient _client;
    private readonly ILogger<ProgrammeVisualizationService> _logger;

    public ProgrammeVisualizationService(
        VisualizationServiceClient client,
        ILogger<ProgrammeVisualizationService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<ProgramVisualizationMap> GetProgramVisualizationMapAsync(
        int volume,
        int educationId,
        string code,
        string language)
    {
        var visualizations = await _client.GetVisualizationsInEducationAsync(educationId);
        var mappedVisualizations = (visualizations ?? [])
            .Select(visualization => new VisualizationItem
            {
                Id = visualization.Id,
                Guid = visualization.Guid,
                NameDanish = visualization.Name?.Danish,
                NameEnglish = visualization.Name?.English
            })
            .OrderBy(visualization => visualization.NameDanish ?? visualization.NameEnglish ?? visualization.Id.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var map = BuildProgramVisualizationMap(volume, educationId, code, language, mappedVisualizations);

        _logger.LogInformation(
            "Mapped programme visualizations volume={Volume}, educationId={EducationId}, code={Code}, coreIds={CoreIds}, packageIds={PackageIds}",
            volume,
            educationId,
            code,
            string.Join(",", map.CoreViews.Select(view => view.Id)),
            string.Join(",", map.RecommendedStudyPackageViews.Select(view => view.Id)));

        return map;
    }

    private static bool IsCoreView(VisualizationItem visualization)
    {
        var name = GetBestName(visualization);

        return string.Equals(name, "Officiel visning", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Studieforløb", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Studieplan", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Ugeskema", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Kompetenceprofil", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Retningsspecifik kompetenceprofil", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportingView(VisualizationItem visualization)
    {
        var name = GetBestName(visualization);

        return string.Equals(name, "Samlingsvisning", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Studieplan + tidl kurser", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "De to første semestre", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Deførstetosemestre", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Min visning", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Regelsamling_test", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Studieplaninkltidlyears", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRecommendedStudyPackageView(VisualizationItem visualization, string code)
    {
        var name = GetBestName(visualization);

        if (string.Equals(code, ElectricalEngineeringCode, StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(name, "ELEKTRO_23_studieforløb", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("EL_", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static string GetBestName(VisualizationItem visualization)
    {
        return visualization.NameDanish ?? visualization.NameEnglish ?? string.Empty;
    }

    private static ProgramVisualizationMap BuildProgramVisualizationMap(
        int volume,
        int educationId,
        string code,
        string language,
        List<VisualizationItem> mappedVisualizations)
    {
        var isElectricalEngineering = string.Equals(code, ElectricalEngineeringCode, StringComparison.OrdinalIgnoreCase);
        var coreViews = mappedVisualizations.Where(IsCoreView).ToList();
        var supportingViews = mappedVisualizations.Where(IsSupportingView).ToList();
        var recommendedPackageViews = mappedVisualizations
            .Where(visualization => IsRecommendedStudyPackageView(visualization, code))
            .ToList();

        var usedIds = coreViews
            .Concat(supportingViews)
            .Concat(recommendedPackageViews)
            .Select(visualization => visualization.Id)
            .ToHashSet();

        var otherViews = mappedVisualizations
            .Where(visualization => !usedIds.Contains(visualization.Id))
            .ToList();

        return new ProgramVisualizationMap
        {
            Volume = volume,
            EducationId = educationId,
            Code = code,
            Language = language,
            ElectricalEngineeringFocused = isElectricalEngineering,
            CoreViews = coreViews,
            SupportingViews = supportingViews,
            RecommendedStudyPackageViews = recommendedPackageViews,
            OtherViews = otherViews
        };
    }
}

public sealed class ProgramVisualizationMap
{
    public int Volume { get; init; }
    public int EducationId { get; init; }
    public string Code { get; init; } = "";
    public string Language { get; init; } = "";
    public bool ElectricalEngineeringFocused { get; init; }
    public List<VisualizationItem> CoreViews { get; init; } = [];
    public List<VisualizationItem> SupportingViews { get; init; } = [];
    public List<VisualizationItem> RecommendedStudyPackageViews { get; init; } = [];
    public List<VisualizationItem> OtherViews { get; init; } = [];
}

public sealed class VisualizationItem
{
    public int Id { get; init; }
    public Guid Guid { get; init; }
    public string? NameDanish { get; init; }
    public string? NameEnglish { get; init; }
}
