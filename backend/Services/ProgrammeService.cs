using Planner.Backend.Models;
using Planner.Backend.Soap.Volumes;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Planner.Backend.Services;

public interface IProgrammeService
{
    Task<ProgrammeListResponse> GetProgrammesAsync(int volume);
    Task<ProgrammeDefinitionResponse?> GetProgrammeDefinitionAsync(int volume, string code, string language);
    Task<ProgrammeStudyFlowOption?> GetStudyFlowAsync(int volume, string code, string optionId, string language);
}

public sealed class ProgrammeService(
    IDtuGateway gateway,
    ICourseCatalogService courseCatalogService,
    IProgrammeVisualizationService programmeVisualizationService,
    IGenericStudyFlowPresetLoader genericStudyFlowPresetLoader,
    IProgrammeClassificationService classificationService,
    DtuCache cache,
    ILogger<ProgrammeService> logger) : IProgrammeService
{
    private readonly IDtuGateway _gateway = gateway;
    private readonly ILogger<ProgrammeService> _logger = logger;

    public async Task<ProgrammeListResponse> GetProgrammesAsync(int volume)
    {
        _ = new AcademicYear(volume);
        var (educations, resolvedVolume) = await ResolveProgrammeCatalogueAsync(volume);
        return new ProgrammeListResponse
        {
            Volume = volume, ResolvedVolume = resolvedVolume,
            Programmes = FlattenProgrammes(educations).OrderBy(p => p.Level)
                .ThenBy(p => p.ProgrammeNameEnglish ?? p.ProgrammeNameDanish ?? p.Code).ToList()
        };
    }

    public async Task<ProgrammeDefinitionResponse?> GetProgrammeDefinitionAsync(int volume, string code, string language)
    {
        ValidateLanguage(language);
        var catalogue = await GetProgrammesAsync(volume);
        var programme = catalogue.Programmes.FirstOrDefault(p => string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase));
        if (programme is null) return null;
        var classification = await classificationService.ResolveAsync(programme, volume);
        var courses = await courseCatalogService.GetCoursesForStudyPlanAsync(volume, classification.MandatoryCourses.Select(c => c.CourseCode));
        var lookup = courses.Courses.ToDictionary(c => c.CourseCode);
        var map = await programmeVisualizationService.GetProgramVisualizationMapAsync(programme.EducationId, programme.Code);
        return new ProgrammeDefinitionResponse
        {
            Volume = volume, ResolvedVolume = catalogue.ResolvedVolume, ClassificationVolume = classification.SourceVolume,
            Language = language, Programme = programme, BucketLimits = ProgrammeRules.ResolveBucketLimits(programme.Level),
            MandatoryCourses = classification.MandatoryCourses.Select(c => new ProgrammeMandatoryCourse
            {
                Bucket = c.Bucket, BucketLabel = c.BucketLabel, SourceOrder = c.SourceOrder,
                Course = lookup.GetValueOrDefault(c.CourseCode) ?? new CourseSummary
                {
                    CourseCode = c.CourseCode, Title = c.Title, Ects = c.Ects,
                    DataWarnings = ["Course details unavailable in the requested catalogue."]
                }
            }).ToList(),
            ApprovedMscElectiveCourseCodes = classification.ApprovedMscElectiveCourseCodes,
            Visualizations = map.CoreViews.Select(ToReference).ToList(),
            RecommendedStudyPackageViews = map.RecommendedStudyPackageViews.Select(ToReference).ToList(),
            StudyFlowOptions = DescribeOptions(programme.Code, map),
            MissingCourseCodes = courses.MissingCourseCodes,
            Notes = [
                $"Programme metadata resolved from volume {catalogue.ResolvedVolume}.",
                classification.SourceVolume is int source
                    ? $"Programme classifications resolved from StudyBoxes in volume {source}."
                    : "No supported StudyBox classifications were found for this programme.",
                "Recommended study flows are loaded when selected. Bucket limits are configured degree-level defaults."
            ]
        };
    }

    public async Task<ProgrammeStudyFlowOption?> GetStudyFlowAsync(int volume, string code, string optionId, string language)
    {
        ValidateLanguage(language);
        var catalogue = await GetProgrammesAsync(volume);
        var programme = catalogue.Programmes.FirstOrDefault(p => string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase));
        if (programme is null) return null;
        if (optionId == "generic-plan")
            return genericStudyFlowPresetLoader.TryLoad(programme.Code, out var preset) ? preset : null;
        var map = await programmeVisualizationService.GetProgramVisualizationMapAsync(programme.EducationId, programme.Code);
        var option = DescribeOptions(programme.Code, map).FirstOrDefault(o => o.Id == optionId);
        if (option?.VisualizationId is not int viewId) return null;
        var html = await _gateway.GetVisualizationAsync(viewId, programme.Code, catalogue.ResolvedVolume, language);
        var resolvedLanguage = language;
        if (string.IsNullOrWhiteSpace(html) && language != "da-DK")
        {
            resolvedLanguage = "da-DK";
            html = await _gateway.GetVisualizationAsync(viewId, programme.Code, catalogue.ResolvedVolume, resolvedLanguage);
        }
        if (string.IsNullOrWhiteSpace(html)) return null;
        // Include content hash so a refreshed HTML entry cannot leave a stale parsed plan behind.
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(html)));
        var parsed = await cache.GetAsync($"parsed-flow:{viewId}:{programme.Code}:{catalogue.ResolvedVolume}:{resolvedLanguage}:{hash}",
            () => Task.FromResult(StudyFlowParser.Parse(html)));
        if (parsed.CoursePlacements.Count == 0) return null;
        var codes = parsed.CoursePlacements.Select(p => p.CourseCode).Distinct().ToList();
        var courses = await courseCatalogService.GetCoursesForStudyPlanAsync(volume, codes);
        var lookup = courses.Courses.ToDictionary(c => c.CourseCode);
        var selections = ResolvePlacementSelections(parsed, lookup);
        foreach (var course in courses.Courses)
        {
            if (ProgrammeRules.ResolvePlacementOverride(programme, course) is { } placementOverride)
                selections[course.CourseCode] = placementOverride.Id;
        }
        return new ProgrammeStudyFlowOption
        {
            Id = option.Id, Label = option.Label, Description = option.Description, Kind = option.Kind,
            VisualizationId = option.VisualizationId, VisualizationGuid = option.VisualizationGuid,
            MissingCourseCodes = courses.MissingCourseCodes,
            SavedPlan = new SavedStudyPlanDto
            {
                SavedAt = DateTimeOffset.UtcNow.ToString("O"), Volume = volume.ToString(CultureInfo.InvariantCulture),
                ImportedCourseCodes = codes, SelectedPlacementByCourseCode = selections,
                Plan = new StudyPlan { Courses = BuildPresetPlannedCourses(parsed, lookup, selections, programme) }
            }
        };
    }

    private List<ProgrammeStudyFlowDescriptor> DescribeOptions(string code, ProgramVisualizationMap map)
    {
        var options = new List<ProgrammeStudyFlowDescriptor>();
        if (genericStudyFlowPresetLoader.TryLoad(code, out var preset) && preset is not null)
            options.Add(new ProgrammeStudyFlowDescriptor { Id = preset.Id, Label = preset.Label, Description = preset.Description, Kind = preset.Kind });
        options.AddRange(map.CoreViews.Where(v => v.NameDanish == "Studieforløb" || v.NameEnglish == "Studieforløb")
            .Concat(map.RecommendedStudyPackageViews).DistinctBy(v => v.Id).Select(v => new ProgrammeStudyFlowDescriptor
            {
                Id = $"view-{v.Id}", Label = v.NameDanish ?? v.NameEnglish ?? $"Study flow {v.Id}",
                Description = "Import the recommended semester placements for this study flow.",
                Kind = v.NameDanish == "Studieforløb" || v.NameEnglish == "Studieforløb" ? "studyFlow" : "recommendedPackage",
                VisualizationId = v.Id, VisualizationGuid = v.Guid
            }));
        return options;
    }

    private static void ValidateLanguage(string language)
    {
        if (language is not ("da-DK" or "en-GB")) throw new ArgumentException("Language must be da-DK or en-GB.");
    }

    private static ProgrammeVisualizationReference ToReference(VisualizationItem item) => new()
    {
        Id = item.Id, Guid = item.Guid, Name = item.NameDanish ?? item.NameEnglish ?? item.Id.ToString(CultureInfo.InvariantCulture)
    };

    private async Task<(IEnumerable<Education> Educations, int ResolvedVolume)> ResolveProgrammeCatalogueAsync(int requestedVolume)
    {
        var educations = await _gateway.GetEducationsAsync(requestedVolume);
        if (FlattenProgrammes(educations).Count > 0)
        {
            return (educations, requestedVolume);
        }

        if (requestedVolume <= 1)
        {
            return (educations, requestedVolume);
        }

        var fallbackVolume = requestedVolume - 1;
        var fallbackEducations = await _gateway.GetEducationsAsync(fallbackVolume);
        if (FlattenProgrammes(fallbackEducations).Count > 0)
        {
            _logger.LogInformation(
                "Programme catalogue fallback: requested volume {RequestedVolume} had no programmes, using {ResolvedVolume}",
                requestedVolume,
                fallbackVolume);

            return (fallbackEducations, fallbackVolume);
        }

        return (educations, requestedVolume);
    }

    private static List<ProgrammeListItem> FlattenProgrammes(IEnumerable<Education>? educations)
    {
        var programmes = new List<ProgrammeListItem>();

        if (educations is null)
        {
            return programmes;
        }

        foreach (var education in educations)
        {
            IEnumerable<Line> lines = education.Lines ?? [];

            foreach (var line in lines)
            {
                var code = (string?)line.Code;
                if (string.IsNullOrWhiteSpace(code))
                {
                    continue;
                }

                programmes.Add(new ProgrammeListItem
                {
                    EducationId = education.Id,
                    EducationStaticGuid = education.StaticGuid,
                    Code = code,
                    Level = InferLevel(education.Name?.English, education.Name?.Danish),
                    EducationNameDanish = education.Name?.Danish,
                    EducationNameEnglish = education.Name?.English,
                    ProgrammeNameDanish = line.Name?.Danish,
                    ProgrammeNameEnglish = line.Name?.English,
                    PopularTitleDanish = line.PopularTitle?.Danish,
                    PopularTitleEnglish = line.PopularTitle?.English,
                    IsInDanish = line.IsInDanish,
                    IsInEnglish = line.IsInEnglish
                });
            }
        }

        return FilterLegacyBscProgrammeDuplicates(programmes);
    }

    private static List<ProgrammeListItem> FilterLegacyBscProgrammeDuplicates(List<ProgrammeListItem> programmes)
    {
        var filtered = new List<ProgrammeListItem>();

        foreach (var group in programmes.GroupBy(programme => programme.Level, StringComparer.OrdinalIgnoreCase))
        {
            if (!string.Equals(group.Key, "bsc", StringComparison.OrdinalIgnoreCase))
            {
                filtered.AddRange(group);
                continue;
            }

            var preferredProgrammes = group
                .Where(programme => Regex.IsMatch(programme.Code, @"(?:23|24)$", RegexOptions.IgnoreCase))
                .ToList();

            foreach (var programme in group)
            {
                var isLegacyCode = !Regex.IsMatch(programme.Code, @"(?:23|24)$", RegexOptions.IgnoreCase);
                if (isLegacyCode && HasReplacementProgramme(programme, preferredProgrammes))
                {
                    continue;
                }

                filtered.Add(programme);
            }
        }

        return filtered;
    }

    private static bool HasReplacementProgramme(ProgrammeListItem legacyProgramme, IReadOnlyCollection<ProgrammeListItem> preferredProgrammes)
    {
        var legacyAliases = BuildProgrammeDuplicateAliases(legacyProgramme);
        if (legacyAliases.Count == 0)
        {
            return false;
        }

        return preferredProgrammes.Any(preferred =>
        {
            if (string.Equals(preferred.Code, legacyProgramme.Code, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var preferredAliases = BuildProgrammeDuplicateAliases(preferred);
            return preferredAliases.Overlaps(legacyAliases);
        });
    }

    private static HashSet<string> BuildProgrammeDuplicateAliases(ProgrammeListItem programme)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddAliases(aliases, programme.PopularTitleEnglish);
        AddAliases(aliases, programme.PopularTitleDanish);
        AddAliases(aliases, programme.ProgrammeNameEnglish);
        AddAliases(aliases, programme.ProgrammeNameDanish);

        if (aliases.Count == 0)
        {
            aliases.Add(ProgrammeClassificationService.NormalizeProgrammeName(programme.Code));
        }

        return aliases;
    }

    private static void AddAliases(HashSet<string> aliases, string? rawValue)
    {
        foreach (var alias in ExpandProgrammeDuplicateAliases(rawValue))
        {
            aliases.Add(alias);
        }
    }

    private static IEnumerable<string> ExpandProgrammeDuplicateAliases(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            yield break;
        }

        var compact = CanonicalizeProgrammeDuplicateText(rawValue);
        if (!string.IsNullOrWhiteSpace(compact))
        {
            yield return compact;
        }

        foreach (Match match in Regex.Matches(
                     rawValue,
                     @"\((?:tidl\.|tidligere|earl\.?|earlier)\s*(?<name>[^)]*?)\)",
                     RegexOptions.IgnoreCase))
        {
            var historical = CanonicalizeProgrammeDuplicateText(match.Groups["name"].Value);
            if (!string.IsNullOrWhiteSpace(historical))
            {
                yield return historical;
            }
        }
    }

    private static string CanonicalizeProgrammeDuplicateText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        normalized = Regex.Replace(normalized, @"\b(?:BSc|Bachelor)\s+in\b", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\bBachelor\s+i\b", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\badmission\s+before\s+september\s+23\b", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\badmiss+sion\s+before\s+september\s+23\b", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\boptag\s+f[øo]r\s+september\s+23\b", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\b20(?:23|24)\b", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\((?:tidl\.|tidligere|earl\.?|earlier)[^)]*\)", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim(' ', '-', ',', '.');

        return ProgrammeClassificationService.NormalizeProgrammeName(normalized);
    }

    private static string InferLevel(string? english, string? danish)
    {
        var combined = $"{english} {danish}";

        if (combined.Contains("Master of Science", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("kandidat", StringComparison.OrdinalIgnoreCase))
        {
            return "msc";
        }

        if (combined.Contains("Bachelor of Engineering", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("diplomingeniør", StringComparison.OrdinalIgnoreCase))
        {
            return "beng";
        }

        if (combined.Contains("Bachelor of Science", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("bachelor", StringComparison.OrdinalIgnoreCase))
        {
            return "bsc";
        }

        return "other";
    }

    private static Dictionary<string, string> ResolvePlacementSelections(
        ParsedStudyFlowPlan parsedPlan,
        IReadOnlyDictionary<string, CourseSummary> courseLookup)
    {
        var selections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var blocksByCourseCode = parsedPlan.CoursePlacements
            .GroupBy(item => item.CourseCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.SelectMany(item => item.TimeBlocks).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(block => block).ToList(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var (courseCode, blocks) in blocksByCourseCode)
        {
            if (!courseLookup.TryGetValue(courseCode, out var course) || course.PlacementOptions.Count == 0)
            {
                continue;
            }

            var matchingOption = course.PlacementOptions.FirstOrDefault(option =>
                option.TimeBlocks
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(block => block)
                    .SequenceEqual(blocks, StringComparer.OrdinalIgnoreCase));

            if (matchingOption is not null)
            {
                selections[courseCode] = matchingOption.Id;
            }
        }

        return selections;
    }

    private static List<PlannedCourse> BuildPresetPlannedCourses(
        ParsedStudyFlowPlan parsedPlan,
        IReadOnlyDictionary<string, CourseSummary> courseLookup,
        IReadOnlyDictionary<string, string> selectedPlacementByCourseCode,
        ProgrammeListItem programme)
    {
        var plannedCourses = new List<PlannedCourse>();

        foreach (var placement in parsedPlan.CoursePlacements)
        {
            var resolvedCourse = courseLookup.TryGetValue(placement.CourseCode, out var enrichedCourse)
                ? enrichedCourse
                : new CourseSummary
                {
                    CourseCode = placement.CourseCode,
                    Title = placement.Title ?? placement.CourseCode,
                    CourseLevel = null,
                    Ects = placement.Ects
                };

            selectedPlacementByCourseCode.TryGetValue(placement.CourseCode, out var placementOptionId);
            var placementOption = resolvedCourse.PlacementOptions.FirstOrDefault(option => option.Id == placementOptionId);

            plannedCourses.Add(new PlannedCourse
            {
                CourseCode = placement.CourseCode,
                Title = resolvedCourse.Title,
                CourseLevel = resolvedCourse.CourseLevel,
                Ects = ResolvePresetCourseEcts(placement, resolvedCourse),
                Semester = placement.Semester,
                Bucket = placement.Bucket,
                PlacementOptionId = placementOption?.Id,
                PlacementOptionLabel = placementOption?.Label,
                GradingMode = resolvedCourse.GradingMode,
                ExaminerMode = resolvedCourse.ExaminerMode,
                TimeBlocks = placement.TimeBlocks.Count > 0 &&
                    ProgrammeRules.ResolvePlacementOverride(programme, resolvedCourse) is null
                    ? placement.TimeBlocks
                    : ResolveFallbackTimeBlocks(resolvedCourse, placement.Semester, placementOption)
            });
        }

        return plannedCourses;
    }

    private static double? ResolvePresetCourseEcts(ParsedStudyFlowPlacement placement, CourseSummary course)
    {
        if (string.Equals(course.CourseCode, "10060", StringComparison.OrdinalIgnoreCase) && placement.Semester % 2 == 0)
        {
            return 0;
        }

        return course.Ects ?? placement.Ects;
    }

    private static List<string> ResolveFallbackTimeBlocks(
        CourseSummary course,
        int semester,
        CoursePlacementOption? placementOption)
    {
        var source = placementOption?.TimeBlocks.Count > 0 == true
            ? placementOption.TimeBlocks
            : course.TimeBlocks;

        return source
            .Where(block => block.StartsWith(semester % 2 == 1 ? "E" : "F", StringComparison.OrdinalIgnoreCase) ||
                            IsSemesterMonthBlock(block, semester))
            .ToList();
    }

    private static bool IsSemesterMonthBlock(string block, int semester)
    {
        var normalized = block.ToUpperInvariant();
        return semester % 2 == 1
            ? normalized == "JANUARY"
            : normalized is "JUNE" or "JULY" or "AUGUST";
    }

}
