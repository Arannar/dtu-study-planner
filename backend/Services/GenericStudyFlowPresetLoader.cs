using System.Text.Json;
using Planner.Backend.Models;

namespace Planner.Backend.Services;

public interface IGenericStudyFlowPresetLoader
{
    bool TryLoad(string programmeCode, out ProgrammeStudyFlowOption? option);
}

public sealed class GenericStudyFlowPresetLoader : IGenericStudyFlowPresetLoader
{
    private const string GenericPlanFileName = "BScEE_generic_plan.json";

    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILogger<GenericStudyFlowPresetLoader> _logger;

    public GenericStudyFlowPresetLoader(
        IHostEnvironment hostEnvironment,
        ILogger<GenericStudyFlowPresetLoader> logger)
    {
        _hostEnvironment = hostEnvironment;
        _logger = logger;
    }

    public bool TryLoad(string programmeCode, out ProgrammeStudyFlowOption? option)
    {
        option = null;

        if (!string.Equals(programmeCode, "ELEKTEK23", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var planPath = ResolveGenericPlanPath();
        if (!File.Exists(planPath))
        {
            _logger.LogWarning(
                "Generic study-flow file not found. Checked content root {ContentRoot} and app base {AppBase}",
                _hostEnvironment.ContentRootPath,
                AppContext.BaseDirectory);
            return false;
        }

        try
        {
            var json = File.ReadAllText(planPath);
            var savedPlan = JsonSerializer.Deserialize<SavedStudyPlanDto>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (savedPlan is null)
            {
                return false;
            }

            option = new ProgrammeStudyFlowOption
            {
                Id = "generic-plan",
                Label = "Generic plan",
                Description = "Imports the curated Electrical Engineering baseline plan from BScEE_generic_plan.json.",
                Kind = "genericPlan",
                SavedPlan = savedPlan
            };

            return true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to load generic plan file for {ProgrammeCode}", programmeCode);
            return false;
        }
    }

    private string ResolveGenericPlanPath()
    {
        var candidates = new[]
        {
            Path.Combine(_hostEnvironment.ContentRootPath, GenericPlanFileName),
            Path.Combine(_hostEnvironment.ContentRootPath, "..", GenericPlanFileName),
            Path.Combine(AppContext.BaseDirectory, GenericPlanFileName),
            Path.Combine(AppContext.BaseDirectory, "..", GenericPlanFileName)
        };

        return candidates
            .Select(Path.GetFullPath)
            .FirstOrDefault(File.Exists)
            ?? Path.GetFullPath(candidates[0]);
    }
}
