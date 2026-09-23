using Planner.Backend.Models;
namespace Planner.Backend.Services;

public static class ProgrammeRules
{
    // Local EE rule supplied by the programme: not yet encoded in DTU's study database.
    // Revisit when the upstream study plans carry the Physics scheme explicitly.
    public static CoursePlacementOption? ResolvePlacementOverride(ProgrammeListItem programme, CourseSummary course) =>
        string.Equals(programme.Code, "ELEKTEK23", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(programme.Level, "bsc", StringComparison.OrdinalIgnoreCase) &&
        course.CourseCode == "10060"
            ? course.PlacementOptions.FirstOrDefault(option => option.Id == "B")
            : null;

    public static readonly string[] CoreVisualizationNames =
    ["Officiel visning", "Studieforløb", "Studieplan", "Ugeskema", "Kompetenceprofil", "Retningsspecifik kompetenceprofil"];

    // A trailing * is a prefix match. Keep programme-specific discovery rules in one place.
    public static readonly IReadOnlyDictionary<string, string[]> RecommendedVisualizationNames =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["ELEKTEK23"] = ["ELEKTRO_23_studieforløb", "EL_*"]
        };

    public static bool IsRecommendedView(string programmeCode, string name) =>
        RecommendedVisualizationNames.TryGetValue(programmeCode, out var patterns) && patterns.Any(pattern =>
            pattern.EndsWith('*') ? name.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase)
                : string.Equals(name, pattern, StringComparison.OrdinalIgnoreCase));

    public static ProgrammeBucketLimits? ResolveBucketLimits(string level)
    {
        if (string.Equals(level, "bsc", StringComparison.OrdinalIgnoreCase))
        {
            return new ProgrammeBucketLimits
            {
                TotalEcts = 180,
                PolytechnicalFoundationEcts = 55,
                ProgrammeSpecificEcts = 55,
                ProjectsEcts = 25,
                ElectivesEcts = 45
            };
        }

        if (string.Equals(level, "msc", StringComparison.OrdinalIgnoreCase))
        {
            return new ProgrammeBucketLimits
            {
                TotalEcts = 120,
                PolytechnicalFoundationEcts = 10,
                ProgrammeSpecificEcts = 50,
                ProjectsEcts = 30,
                ElectivesEcts = 30
            };
        }

        if (string.Equals(level, "beng", StringComparison.OrdinalIgnoreCase))
        {
            return new ProgrammeBucketLimits
            {
                TotalEcts = 210,
                ElectivesEcts = 30,
                ProjectsEcts = 15,
                MandatoryEcts = 135,
                InternshipEcts = 30
            };
        }

        return null;
    }

}
