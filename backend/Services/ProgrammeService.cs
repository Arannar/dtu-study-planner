using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Planner.Backend.Models;
using Planner.Backend.Soap.Courseblocks;
using Planner.Backend.Soap.Visualizations;
using Planner.Backend.Soap.Volumes;

namespace Planner.Backend.Services;

public interface IProgrammeService
{
    Task<ProgrammeListResponse> GetProgrammesAsync(int volume);
    Task<ProgrammeDefinitionResponse?> GetProgrammeDefinitionAsync(int volume, string code, string language);
}

public sealed class ProgrammeService : IProgrammeService
{
    private const string GenericPlanFileName = "BScEE_generic_plan.json";

    private static readonly Regex StrongParagraphRegex = new(
        @"<p>\s*<strong>\s*(?<heading>.*?)\s*</strong>\s*</p>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex TableRowRegex = new(
        @"<tr\b[^>]*>(?<row>.*?)</tr>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex TableCellRegex = new(
        @"<td\b[^>]*>(?<cell>.*?)</td>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex CourseCodeRegex = new(
        @"\b(?<code>\d{5})\b",
        RegexOptions.Compiled);

    private static readonly Regex SemesterTermRegex = new(
        @"<div class=""term""><div class=""header""><div class=""rotate\s*"">(?<semester>\d+)\.Semester</div></div><div class=""itemcontainer"">(?<items>.*?)</div></div>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex ItemBlockRegex = new(
        @"<div class=""item\b[^""]*(?<bucket>subject-Bachelor23_[^""\s]+)[^""]*""[^>]*>(?<item>.*?)</div></div>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex TitleRegex = new(
        @"<strong\b[^>]*>(?<title>.*?)</strong>|<span class=""title\s*"">(?<titleOnly>.*?)</span>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex PointRegex = new(
        @"<div class=""point\s*"">(?<points>.*?)</div>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex SemesterScheduleRegex = new(
        @"<p>\s*<strong>\s*(?<semester>\d+)\.\s*sem(?:ester|ster)\s*</strong>\s*</p>\s*<div class=""schema\s*"">(?<schema>.*?)</div>\s*(?<tail>.*?)(?=(<p>\s*<strong>\s*\d+\.\s*sem(?:ester|ster)\s*</strong>\s*</p>)|$)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex TimetableCourseRegex = new(
        @"<div class=""(?<period>early|late|night)\s+dayinfo\s+(?<day>monday|tuesday|wednesday|thursday|friday)"">\s*<div class=""course[^""]*"">\s*<a href=""https://kurser\.dtu\.dk/course/\d{4}-\d{4}/(?<code>\d{5})",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex TableRowWithCourseRegex = new(
        @"<tr\b[^>]*>(?<row>.*?)</tr>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex TimeBlockTokenRegex = new(
        @"\b(?<block>[EF]\d[AB]?|JANUARY|JUNE|JULY|AUGUST|Januar|Juni|Juli|August)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MonthTokenRegex = new(
        @"\b(?<month>Januar|Juni|Juli|August)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly VolumeServiceClient _volumeClient;
    private readonly VisualizationServiceClient _visualizationClient;
    private readonly CourseblockServiceClient _courseblockClient;
    private readonly ICourseCatalogService _courseCatalogService;
    private readonly IProgrammeVisualizationService _programmeVisualizationService;
    private readonly IGenericStudyFlowPresetLoader _genericStudyFlowPresetLoader;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILogger<ProgrammeService> _logger;

    public ProgrammeService(
        VolumeServiceClient volumeClient,
        VisualizationServiceClient visualizationClient,
        CourseblockServiceClient courseblockClient,
        ICourseCatalogService courseCatalogService,
        IProgrammeVisualizationService programmeVisualizationService,
        IGenericStudyFlowPresetLoader genericStudyFlowPresetLoader,
        IHostEnvironment hostEnvironment,
        ILogger<ProgrammeService> logger)
    {
        _volumeClient = volumeClient;
        _visualizationClient = visualizationClient;
        _courseblockClient = courseblockClient;
        _courseCatalogService = courseCatalogService;
        _programmeVisualizationService = programmeVisualizationService;
        _genericStudyFlowPresetLoader = genericStudyFlowPresetLoader;
        _hostEnvironment = hostEnvironment;
        _logger = logger;
    }

    public async Task<ProgrammeListResponse> GetProgrammesAsync(int volume)
    {
        var (educations, resolvedVolume) = await ResolveProgrammeCatalogueAsync(volume);
        var programmes = FlattenProgrammes(educations)
            .OrderBy(programme => programme.Level, StringComparer.OrdinalIgnoreCase)
            .ThenBy(programme => programme.ProgrammeNameEnglish ?? programme.ProgrammeNameDanish ?? programme.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ProgrammeListResponse
        {
            Volume = volume,
            ResolvedVolume = resolvedVolume,
            Programmes = programmes
        };
    }

    public async Task<ProgrammeDefinitionResponse?> GetProgrammeDefinitionAsync(int volume, string code, string language)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var (educations, resolvedVolume) = await ResolveProgrammeCatalogueAsync(volume);
        var programme = FlattenProgrammes(educations)
            .FirstOrDefault(item => string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase));

        if (programme is null)
        {
            return null;
        }

        var visualizationMap = await _programmeVisualizationService.GetProgramVisualizationMapAsync(resolvedVolume, programme.EducationId, programme.Code, language);
        var studyPlanView = FindVisualization(visualizationMap.CoreViews, "Studieplan");
        var studyFlowView = FindVisualization(visualizationMap.CoreViews, "Studieforløb");
        var weeklyScheduleView = FindVisualization(visualizationMap.CoreViews, "Ugeskema");
        var officialView = FindVisualization(visualizationMap.CoreViews, "Officiel visning");

        var mandatoryCourses = new List<ProgrammeMandatoryCourse>();
        var missingCourseCodes = new List<string>();
        var approvedMscElectiveCourseCodes = new List<string>();
        int? studyBoxSourceVolume = null;

        List<ParsedProgrammeCourse> parsedMandatoryCourses;
        if (programme.Level is "bsc" or "beng" or "msc")
        {
            var studyBoxClassification = await ResolveProgrammeClassificationFromStudyBoxesAsync(programme, volume);
            studyBoxSourceVolume = studyBoxClassification.SourceVolume;
            parsedMandatoryCourses = studyBoxClassification.MandatoryCourses;
            approvedMscElectiveCourseCodes = studyBoxClassification.ApprovedMscElectiveCourseCodes;
        }
        else
        {
            parsedMandatoryCourses = new List<ParsedProgrammeCourse>();
        }

        if (programme.Level is not ("bsc" or "beng" or "msc")
            && parsedMandatoryCourses.Count == 0
            && studyPlanView is not null)
        {
            var studyPlanHtml = await GetVisualizationWithLanguageFallbackAsync(studyPlanView.Id, programme.Code, resolvedVolume, language);
            parsedMandatoryCourses = ParseMandatoryCoursesFromStudyPlan(studyPlanHtml ?? string.Empty);
        }

        if (parsedMandatoryCourses.Count > 0)
        {
            var catalogResponse = await _courseCatalogService.GetCoursesForStudyPlanAsync(
                volume,
                parsedMandatoryCourses.Select(course => course.CourseCode));

            var courseLookup = catalogResponse.Courses.ToDictionary(course => course.CourseCode, StringComparer.OrdinalIgnoreCase);
            missingCourseCodes.AddRange(catalogResponse.MissingCourseCodes);

            mandatoryCourses = parsedMandatoryCourses
                .Select(course => new ProgrammeMandatoryCourse
                {
                    Bucket = course.Bucket,
                    BucketLabel = course.BucketLabel,
                    SourceOrder = course.SourceOrder,
                    Course = BuildMergedCourseSummary(course, courseLookup)
                })
                .ToList();
        }

        var studyFlowOptions = new List<ProgrammeStudyFlowOption>();

        if (_genericStudyFlowPresetLoader.TryLoad(programme.Code, out var genericPlanOption) && genericPlanOption is not null)
        {
            studyFlowOptions.Add(genericPlanOption);
        }

        var studyFlowCandidates = new List<VisualizationItem>();
        if (studyFlowView is not null)
        {
            studyFlowCandidates.Add(studyFlowView);
        }

        studyFlowCandidates.AddRange(visualizationMap.RecommendedStudyPackageViews);

        foreach (var candidate in studyFlowCandidates
                     .GroupBy(item => item.Id)
                     .Select(group => group.First())
                     .OrderBy(item => GetStudyFlowOptionSortKey(item, studyFlowView)))
        {
            var option = await BuildStudyFlowOptionAsync(
                candidate,
                programme,
                resolvedVolume,
                volume,
                language,
                missingCourseCodes);

            if (option is not null)
            {
                studyFlowOptions.Add(option);
            }
        }

        return new ProgrammeDefinitionResponse
        {
            Volume = volume,
            ResolvedVolume = resolvedVolume,
            Language = language,
            Programme = programme,
            BucketLimits = ResolveBucketLimits(programme.Level),
            MandatoryCourses = mandatoryCourses,
            ApprovedMscElectiveCourseCodes = approvedMscElectiveCourseCodes,
            Visualizations = BuildVisualizationReferences(officialView, studyPlanView, studyFlowView, weeklyScheduleView),
            RecommendedStudyPackageViews = visualizationMap.RecommendedStudyPackageViews
                .Select(ToReference)
                .ToList(),
            StudyFlowOptions = studyFlowOptions,
            MissingCourseCodes = missingCourseCodes
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Notes = BuildProgrammeDefinitionNotes(programme.Code, resolvedVolume, studyBoxSourceVolume, studyPlanView is not null, studyFlowView is not null, mandatoryCourses.Count, studyFlowOptions.Count)
        };
    }

    private async Task<(IEnumerable<dynamic> Educations, int ResolvedVolume)> ResolveProgrammeCatalogueAsync(int requestedVolume)
    {
        var educations = await _volumeClient.GetEducationsInVolumeAsync(requestedVolume);
        if (FlattenProgrammes(educations).Count > 0)
        {
            return (educations, requestedVolume);
        }

        if (requestedVolume <= 1)
        {
            return (educations, requestedVolume);
        }

        var fallbackVolume = requestedVolume - 1;
        var fallbackEducations = await _volumeClient.GetEducationsInVolumeAsync(fallbackVolume);
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

    private static List<ProgrammeListItem> FlattenProgrammes(IEnumerable<dynamic>? educations)
    {
        var programmes = new List<ProgrammeListItem>();

        if (educations is null)
        {
            return programmes;
        }

        foreach (var education in educations)
        {
            IEnumerable<dynamic> lines = education.Lines is IEnumerable<dynamic> typedLines
                ? typedLines
                : Enumerable.Empty<dynamic>();

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
            aliases.Add(NormalizeProgrammeName(programme.Code));
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

        return NormalizeProgrammeName(normalized);
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

    private bool TryLoadGenericPlanOption(string programmeCode, out ProgrammeStudyFlowOption? option)
    {
        option = null;

        if (!string.Equals(programmeCode, "ELEKTEK23", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var planPath = Path.GetFullPath(Path.Combine(_hostEnvironment.ContentRootPath, "..", GenericPlanFileName));
        if (!File.Exists(planPath))
        {
            _logger.LogWarning("Generic study-flow file not found at {Path}", planPath);
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

    private async Task<ProgrammeStudyFlowOption?> BuildStudyFlowOptionAsync(
        VisualizationItem visualization,
        ProgrammeListItem programme,
        int visualizationVolume,
        int courseVolume,
        string language,
        List<string> missingCourseCodes)
    {
        var html = await GetVisualizationWithLanguageFallbackAsync(visualization.Id, programme.Code, visualizationVolume, language);
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var parsedPlan = ParseStudyFlowPlan(html);
        if (parsedPlan.CoursePlacements.Count == 0)
        {
            return null;
        }

        var uniqueCodes = parsedPlan.CoursePlacements
            .Select(item => item.CourseCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var catalogResponse = await _courseCatalogService.GetCoursesForStudyPlanAsync(courseVolume, uniqueCodes);
        var courseLookup = catalogResponse.Courses.ToDictionary(course => course.CourseCode, StringComparer.OrdinalIgnoreCase);
        missingCourseCodes.AddRange(catalogResponse.MissingCourseCodes);

        var selectedPlacementByCourseCode = ResolvePlacementSelections(parsedPlan, courseLookup);
        var plannedCourses = BuildPresetPlannedCourses(parsedPlan, courseLookup, selectedPlacementByCourseCode);

        if (plannedCourses.Count == 0)
        {
            return null;
        }

        return new ProgrammeStudyFlowOption
        {
            Id = Slugify(visualization.NameDanish ?? visualization.NameEnglish ?? visualization.Id.ToString(CultureInfo.InvariantCulture)),
            Label = visualization.NameDanish ?? visualization.NameEnglish ?? "Study flow",
            Description = BuildStudyFlowDescription(visualization),
            Kind = studyFlowViewKind(visualization),
            VisualizationId = visualization.Id,
            VisualizationGuid = visualization.Guid,
            SavedPlan = new SavedStudyPlanDto
            {
                SavedAt = DateTimeOffset.UtcNow.ToString("O"),
                Volume = courseVolume.ToString(CultureInfo.InvariantCulture),
                ImportedCourseCodes = uniqueCodes,
                SelectedPlacementByCourseCode = selectedPlacementByCourseCode,
                Plan = new StudyPlan
                {
                    Courses = plannedCourses
                }
            }
        };

        static string studyFlowViewKind(VisualizationItem candidate)
        {
            var name = candidate.NameDanish ?? candidate.NameEnglish ?? string.Empty;
            return string.Equals(name, "Studieforløb", StringComparison.OrdinalIgnoreCase)
                ? "studyFlow"
                : "recommendedPackage";
        }
    }

    private ParsedStudyFlowPlan ParseStudyFlowPlan(string html)
    {
        var placements = new List<ParsedStudyFlowPlacement>();
        var placementByCourseCode = new Dictionary<string, ParsedStudyFlowPlacement>(StringComparer.OrdinalIgnoreCase);

        foreach (Match semesterMatch in SemesterTermRegex.Matches(html))
        {
            if (!int.TryParse(semesterMatch.Groups["semester"].Value, out var semester))
            {
                continue;
            }

            var itemsHtml = semesterMatch.Groups["items"].Value;
            foreach (Match itemMatch in ItemBlockRegex.Matches(itemsHtml))
            {
                var itemHtml = itemMatch.Groups["item"].Value;
                var codeMatch = CourseCodeRegex.Match(itemHtml);
                if (!codeMatch.Success)
                {
                    continue;
                }

                var courseCode = codeMatch.Groups["code"].Value;
                var placement = GetOrCreateStudyFlowPlacement(placementByCourseCode, placements, courseCode, semester);
                placement.Bucket = MapStudyFlowBucket(itemMatch.Groups["bucket"].Value) ?? placement.Bucket;
                placement.Title ??= ExtractItemTitle(itemHtml);
                placement.Ects ??= TryParseEcts(ExtractItemPoints(itemHtml));
            }
        }

        foreach (Match scheduleMatch in SemesterScheduleRegex.Matches(html))
        {
            if (!int.TryParse(scheduleMatch.Groups["semester"].Value, out var semester))
            {
                continue;
            }

            var schemaHtml = scheduleMatch.Groups["schema"].Value;
            foreach (Match timetableMatch in TimetableCourseRegex.Matches(schemaHtml))
            {
                var courseCode = timetableMatch.Groups["code"].Value;
                var placement = GetOrCreateStudyFlowPlacement(placementByCourseCode, placements, courseCode, semester);
                var block = ToSemesterBlock(
                    semester,
                    timetableMatch.Groups["day"].Value,
                    timetableMatch.Groups["period"].Value);

                if (!string.IsNullOrWhiteSpace(block))
                {
                    placement.TimeBlocks.Add(block);
                }
            }

            var tailHtml = scheduleMatch.Groups["tail"].Value;
            foreach (Match rowMatch in TableRowWithCourseRegex.Matches(tailHtml))
            {
                var rowHtml = rowMatch.Groups["row"].Value;
                var codeMatch = CourseCodeRegex.Match(rowHtml);
                if (!codeMatch.Success)
                {
                    continue;
                }

                var courseCode = codeMatch.Groups["code"].Value;
                var placement = GetOrCreateStudyFlowPlacement(placementByCourseCode, placements, courseCode, semester);
                foreach (var block in ExtractTimeBlocksFromTableRow(rowHtml, semester))
                {
                    placement.TimeBlocks.Add(block);
                }

                placement.Title ??= ExtractItemTitle(rowHtml);
                placement.Ects ??= TryParseEcts(ExtractItemPoints(rowHtml));
            }
        }

        foreach (var placement in placements)
        {
            placement.TimeBlocks = placement.TimeBlocks
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(block => block, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return new ParsedStudyFlowPlan
        {
            CoursePlacements = placements
                .Where(item => item.Semester is >= 1 and <= 6)
                .OrderBy(item => item.Semester)
                .ThenBy(item => item.SourceOrder)
                .ToList()
        };
    }

    private async Task<string> GetVisualizationWithLanguageFallbackAsync(
        int visualizationId,
        string programmeCode,
        int volume,
        string language)
    {
        var html = await _visualizationClient.GetVisualizationAsync(visualizationId, programmeCode, volume, language) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(html))
        {
            return html;
        }

        if (string.Equals(language, "da-DK", StringComparison.OrdinalIgnoreCase))
        {
            return html;
        }

        var fallbackHtml = await _visualizationClient.GetVisualizationAsync(visualizationId, programmeCode, volume, "da-DK") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(fallbackHtml))
        {
            _logger.LogInformation(
                "Visualization {VisualizationId} for {ProgrammeCode} was empty in {Language}; falling back to da-DK",
                visualizationId,
                programmeCode,
                language);
        }

        return fallbackHtml;
    }

    private async Task<ProgrammeStudyBoxClassification> ResolveProgrammeClassificationFromStudyBoxesAsync(
        ProgrammeListItem programme,
        int requestedVolume)
    {
        foreach (var candidateVolume in BuildStudyBoxVolumeCandidates(requestedVolume))
        {
            var studyBoxes = await _courseblockClient.GetStudyBoxInfoByVolumesAsync([candidateVolume]);
            var classification = ParseProgrammeClassificationFromStudyBoxes(programme, studyBoxes ?? []);
            if (classification.HasData)
            {
                _logger.LogInformation(
                    "Resolved StudyBox classification programme={ProgrammeCode}, level={Level}, volume={Volume}, mandatoryCount={MandatoryCount}, approvedMscCount={ApprovedMscCount}",
                    programme.Code,
                    programme.Level,
                    candidateVolume,
                    classification.MandatoryCourses.Count,
                    classification.ApprovedMscElectiveCourseCodes.Count);

                return classification with { SourceVolume = candidateVolume };
            }
        }

        return new ProgrammeStudyBoxClassification();
    }

    private static IEnumerable<int> BuildStudyBoxVolumeCandidates(int requestedVolume)
    {
        if (requestedVolume > 0)
        {
            yield return requestedVolume;
        }

        if (requestedVolume > 1)
        {
            yield return requestedVolume - 1;
        }
    }

    private static ParsedStudyFlowPlacement GetOrCreateStudyFlowPlacement(
        Dictionary<string, ParsedStudyFlowPlacement> placementByCourseCode,
        List<ParsedStudyFlowPlacement> placements,
        string courseCode,
        int semester)
    {
        var key = $"{courseCode}:{semester}";
        if (!placementByCourseCode.TryGetValue(key, out var placement))
        {
            placement = new ParsedStudyFlowPlacement
            {
                CourseCode = courseCode,
                Semester = semester,
                SourceOrder = placements.Count + 1
            };

            placementByCourseCode[key] = placement;
            placements.Add(placement);
        }

        return placement;
    }

    private static string? MapStudyFlowBucket(string rawBucket)
    {
        return rawBucket.ToLowerInvariant() switch
        {
            var value when value.Contains("polytechnic", StringComparison.Ordinal) => "polytechnicalFoundation",
            var value when value.Contains("programmespecific", StringComparison.Ordinal) => "programmeSpecific",
            var value when value.Contains("project", StringComparison.Ordinal) => "projects",
            var value when value.Contains("elective", StringComparison.Ordinal) => "electives",
            _ => null
        };
    }

    private static bool IsMandatoryProgrammeBucket(string bucket)
    {
        return bucket is "polytechnicalFoundation" or "programmeSpecific" or "projects";
    }

    private static ProgrammeStudyBoxClassification ParseProgrammeClassificationFromStudyBoxes(
        ProgrammeListItem programme,
        IEnumerable<StudyBoxInfo> studyBoxes)
    {
        return programme.Level.ToLowerInvariant() switch
        {
            "bsc" => ParseBscProgrammeClassificationFromStudyBoxes(programme, studyBoxes),
            "beng" => ParseBengProgrammeClassificationFromStudyBoxes(programme, studyBoxes),
            "msc" => ParseMscProgrammeClassificationFromStudyBoxes(programme, studyBoxes),
            _ => new ProgrammeStudyBoxClassification()
        };
    }

    private static ProgrammeStudyBoxClassification ParseBscProgrammeClassificationFromStudyBoxes(
        ProgrammeListItem programme,
        IEnumerable<StudyBoxInfo> studyBoxes)
    {
        var aliases = BuildProgrammeAliases(programme);
        var mandatory = new List<ParsedProgrammeCourse>();
        var approvedMsc = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var box in studyBoxes ?? [])
        {
            var courseCodes = NormalizeStudyBoxCourseCodes(box.CourseCodes);
            if (courseCodes.Count == 0)
            {
                continue;
            }

            if (TryParseBscMandatoryStudyBox(programme, aliases, box, out var parsedMandatory))
            {
                mandatory.AddRange(courseCodes.Select(courseCode => new ParsedProgrammeCourse
                {
                    CourseCode = courseCode,
                    Title = courseCode,
                    Bucket = parsedMandatory.Bucket,
                    BucketLabel = parsedMandatory.Label,
                    SourceOrder = mandatory.Count + 1
                }));
                continue;
            }

            if (TryParseBscApprovedMscStudyBox(aliases, box))
            {
                foreach (var courseCode in courseCodes)
                {
                    approvedMsc.Add(courseCode);
                }
            }
        }

        return new ProgrammeStudyBoxClassification
        {
            MandatoryCourses = mandatory
                .GroupBy(item => item.CourseCode, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(item => item.SourceOrder)
                .ToList(),
            ApprovedMscElectiveCourseCodes = approvedMsc
                .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private static ProgrammeStudyBoxClassification ParseBengProgrammeClassificationFromStudyBoxes(
        ProgrammeListItem programme,
        IEnumerable<StudyBoxInfo> studyBoxes)
    {
        var aliases = BuildProgrammeAliases(programme);
        var mandatory = new List<ParsedProgrammeCourse>();

        foreach (var box in studyBoxes ?? [])
        {
            var courseCodes = NormalizeStudyBoxCourseCodes(box.CourseCodes);
            if (courseCodes.Count == 0)
            {
                continue;
            }

            if (!TryParseBengMandatoryStudyBox(aliases, box, out var label))
            {
                continue;
            }

            mandatory.AddRange(courseCodes.Select(courseCode => new ParsedProgrammeCourse
            {
                CourseCode = courseCode,
                Title = courseCode,
                Bucket = "mandatory",
                BucketLabel = label,
                SourceOrder = mandatory.Count + 1
            }));
        }

        return new ProgrammeStudyBoxClassification
        {
            MandatoryCourses = mandatory
                .GroupBy(item => item.CourseCode, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(item => item.SourceOrder)
                .ToList()
        };
    }

    private static ProgrammeStudyBoxClassification ParseMscProgrammeClassificationFromStudyBoxes(
        ProgrammeListItem programme,
        IEnumerable<StudyBoxInfo> studyBoxes)
    {
        var aliases = BuildProgrammeAliases(programme);
        var mandatory = new List<ParsedProgrammeCourse>();

        foreach (var box in studyBoxes ?? [])
        {
            var courseCodes = NormalizeStudyBoxCourseCodes(box.CourseCodes);
            if (courseCodes.Count == 0)
            {
                continue;
            }

            if (!TryParseMscMandatoryStudyBox(aliases, box, out var parsedMandatory))
            {
                continue;
            }

            mandatory.AddRange(courseCodes.Select(courseCode => new ParsedProgrammeCourse
            {
                CourseCode = courseCode,
                Title = courseCode,
                Bucket = parsedMandatory.Bucket,
                BucketLabel = parsedMandatory.Label,
                SourceOrder = mandatory.Count + 1
            }));
        }

        return new ProgrammeStudyBoxClassification
        {
            MandatoryCourses = mandatory
                .GroupBy(item => item.CourseCode, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(item => item.SourceOrder)
                .ToList()
        };
    }

    private static HashSet<string> BuildProgrammeAliases(ProgrammeListItem programme)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddAlias(aliases, programme.ProgrammeNameDanish);
        AddAlias(aliases, programme.ProgrammeNameEnglish);
        AddAlias(aliases, programme.PopularTitleDanish);
        AddAlias(aliases, programme.PopularTitleEnglish);

        return aliases;

        static void AddAlias(HashSet<string> target, string? value)
        {
            var normalized = NormalizeProgrammeName(value);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                target.Add(normalized);
            }
        }
    }

    private static List<string> NormalizeStudyBoxCourseCodes(IEnumerable<string>? courseCodes)
    {
        return (courseCodes ?? [])
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Where(code => Regex.IsMatch(code, @"^\d{5}$"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool TryParseBscMandatoryStudyBox(
        ProgrammeListItem programme,
        IReadOnlyCollection<string> aliases,
        StudyBoxInfo box,
        out (string Bucket, string Label) parsed)
    {
        parsed = default;

        foreach (var label in EnumerateStudyBoxLabels(box))
        {
            if (TryParseStrictProgrammeStudyBoxLabel(
                    label,
                    "Polyteknisk grundlag (BSc), ",
                    aliases,
                    out _))
            {
                parsed = ("polytechnicalFoundation", label.Trim());
                return true;
            }

            if (TryParseStrictProgrammeStudyBoxLabel(
                    label,
                    "Polytechnical foundation (BSc), ",
                    aliases,
                    out _))
            {
                parsed = ("polytechnicalFoundation", label.Trim());
                return true;
            }

            if (TryParseStrictProgrammeStudyBoxLabel(
                    label,
                    "Retningsspecifikt kursus (BSc), ",
                    aliases,
                    out _))
            {
                parsed = ("programmeSpecific", label.Trim());
                return true;
            }

            if (TryParseStrictProgrammeStudyBoxLabel(
                    label,
                    "Programme specific course (BSc), ",
                    aliases,
                    out _))
            {
                parsed = ("programmeSpecific", label.Trim());
                return true;
            }

            if (TryParseStrictProgrammeStudyBoxLabel(
                    label,
                    "Projekter (BSc), ",
                    aliases,
                    out _))
            {
                parsed = ("projects", label.Trim());
                return true;
            }

            if (TryParseStrictProgrammeStudyBoxLabel(
                    label,
                    "Projects (BSc), ",
                    aliases,
                    out _))
            {
                parsed = ("projects", label.Trim());
                return true;
            }
        }

        return false;
    }

    private static bool TryParseBscApprovedMscStudyBox(
        IReadOnlyCollection<string> aliases,
        StudyBoxInfo box)
    {
        return EnumerateStudyBoxLabels(box).Any(label =>
            TryParseStrictProgrammeStudyBoxLabel(label, "Valgfrit kandidatkursus (BSc), ", aliases, out _) ||
            TryParseStrictProgrammeStudyBoxLabel(label, "Elective MSc course (BSc), ", aliases, out _));
    }

    private static bool TryParseBengMandatoryStudyBox(
        IReadOnlyCollection<string> aliases,
        StudyBoxInfo box,
        out string label)
    {
        label = string.Empty;

        foreach (var candidateLabel in EnumerateStudyBoxLabels(box))
        {
            if (TryParseStrictProgrammeStudyBoxLabel(candidateLabel, "Obligatorisk kursus (B Eng), ", aliases, out _) ||
                TryParseStrictProgrammeStudyBoxLabel(candidateLabel, "Mandatory course (B Eng), ", aliases, out _) ||
                TryParseStrictProgrammeStudyBoxLabel(candidateLabel, "Mandatory course, Bachelor of Engineering ", aliases, out _))
            {
                label = candidateLabel.Trim();
                return true;
            }
        }

        return false;
    }

    private static bool TryParseMscMandatoryStudyBox(
        IReadOnlyCollection<string> aliases,
        StudyBoxInfo box,
        out (string Bucket, string Label) parsed)
    {
        parsed = default;

        foreach (var label in EnumerateStudyBoxLabels(box))
        {
            if (TryParseStrictProgrammeStudyBoxLabel(label, "Polyteknisk grundlag (MSc), ", aliases, out _) ||
                TryParseStrictProgrammeStudyBoxLabel(label, "Polytechnical foundation (MSc), ", aliases, out _))
            {
                parsed = ("polytechnicalFoundation", label.Trim());
                return true;
            }

            if (TryParseStrictProgrammeStudyBoxLabel(label, "Retningsspecifikt kursus (MSc), ", aliases, out _) ||
                TryParseStrictProgrammeStudyBoxLabel(label, "Programme specific course (MSc), ", aliases, out _))
            {
                parsed = ("programmeSpecific", label.Trim());
                return true;
            }

            if (TryParseStrictProgrammeStudyBoxLabel(label, "Projekter (MSc), ", aliases, out _) ||
                TryParseStrictProgrammeStudyBoxLabel(label, "Projects (MSc), ", aliases, out _))
            {
                parsed = ("projects", label.Trim());
                return true;
            }
        }

        return false;
    }

    private static bool TryParseStrictProgrammeStudyBoxLabel(
        string? label,
        string requiredPrefix,
        IReadOnlyCollection<string> aliases,
        out string programmeName)
    {
        programmeName = string.Empty;

        if (string.IsNullOrWhiteSpace(label) || aliases.Count == 0)
        {
            return false;
        }

        var trimmedLabel = label.Trim();
        if (!trimmedLabel.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        programmeName = NormalizeProgrammeName(trimmedLabel[requiredPrefix.Length..]);
        return aliases.Contains(programmeName);
    }

    private static IEnumerable<string> EnumerateStudyBoxLabels(StudyBoxInfo box)
    {
        if (!string.IsNullOrWhiteSpace(box.Danish))
        {
            yield return box.Danish;
        }

        if (!string.IsNullOrWhiteSpace(box.English))
        {
            yield return box.English;
        }
    }

    private static string NormalizeProgrammeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value
            .Replace("&", " og ", StringComparison.Ordinal)
            .Replace(" and ", " og ", StringComparison.OrdinalIgnoreCase)
            .Replace("-", " ", StringComparison.Ordinal)
            .Replace(".", " ", StringComparison.Ordinal)
            .Replace(",", " ", StringComparison.Ordinal);

        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
        return normalized.ToLowerInvariant();
    }

    private static string? ExtractItemTitle(string html)
    {
        var match = TitleRegex.Match(html);
        if (!match.Success)
        {
            return null;
        }

        var title = match.Groups["title"].Success
            ? match.Groups["title"].Value
            : match.Groups["titleOnly"].Value;

        return NormalizeText(title);
    }

    private static string? ExtractItemPoints(string html)
    {
        var match = PointRegex.Match(html);
        return match.Success ? NormalizeText(match.Groups["points"].Value) : null;
    }

    private static IEnumerable<string> ExtractTimeBlocksFromTableRow(string rowHtml, int semester)
    {
        var blocks = new List<string>();

        foreach (Match blockMatch in TimeBlockTokenRegex.Matches(NormalizeText(rowHtml)))
        {
            var value = blockMatch.Groups["block"].Value.ToUpperInvariant();
            if (value.StartsWith('E') || value.StartsWith('F'))
            {
                blocks.Add(value);
            }
        }

        foreach (Match monthMatch in MonthTokenRegex.Matches(NormalizeText(rowHtml)))
        {
            blocks.Add(monthMatch.Groups["month"].Value.ToUpperInvariant() switch
            {
                "JANUAR" => "JANUARY",
                "JUNI" => "JUNE",
                "JULI" => "JULY",
                "AUGUST" => "AUGUST",
                _ => monthMatch.Groups["month"].Value.ToUpperInvariant()
            });
        }

        return blocks
            .Select(block => NormalizeSemesterPrefixedBlock(block, semester))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeSemesterPrefixedBlock(string block, int semester)
    {
        if (block is "JANUARY" or "JUNE" or "JULY" or "AUGUST")
        {
            return block;
        }

        if (block.StartsWith('E') || block.StartsWith('F'))
        {
            return block;
        }

        var prefix = semester % 2 == 1 ? "E" : "F";
        return $"{prefix}{block}";
    }

    private static string? ToSemesterBlock(int semester, string day, string period)
    {
        var displayBlock = (day.ToLowerInvariant(), period.ToLowerInvariant()) switch
        {
            ("monday", "early") => "1A",
            ("tuesday", "early") => "3A",
            ("wednesday", "early") => "5A",
            ("thursday", "early") => "2B",
            ("friday", "early") => "4B",
            ("monday", "late") => "2A",
            ("tuesday", "late") => "4A",
            ("wednesday", "late") => "5B",
            ("thursday", "late") => "1B",
            ("friday", "late") => "3B",
            _ => null
        };

        if (displayBlock is null)
        {
            return null;
        }

        return semester % 2 == 1 ? $"E{displayBlock}" : $"F{displayBlock}";
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
        IReadOnlyDictionary<string, string> selectedPlacementByCourseCode)
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
                PlacementOptionId = placementOption?.Id,
                PlacementOptionLabel = placementOption?.Label,
                GradingMode = resolvedCourse.GradingMode,
                ExaminerMode = resolvedCourse.ExaminerMode,
                TimeBlocks = placement.TimeBlocks.Count > 0
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

    private static int GetStudyFlowOptionSortKey(VisualizationItem candidate, VisualizationItem? studyFlowView)
    {
        if (studyFlowView is not null && candidate.Id == studyFlowView.Id)
        {
            return 0;
        }

        return 1;
    }

    private static string BuildStudyFlowDescription(VisualizationItem visualization)
    {
        var name = visualization.NameDanish ?? visualization.NameEnglish ?? string.Empty;
        return string.Equals(name, "Studieforløb", StringComparison.OrdinalIgnoreCase)
            ? "Imports the generic recommended semester-by-semester study flow for the selected programme."
            : $"Imports the recommended package shown in {name}.";
    }

    private static string Slugify(string value)
    {
        var normalized = Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? "study-flow-option" : normalized;
    }

    private List<ParsedProgrammeCourse> ParseMandatoryCoursesFromStudyPlan(string html)
    {
        var classBasedResults = ParseMandatoryCoursesFromClassBasedStudyPlan(html);
        if (classBasedResults.Count > 0)
        {
            _logger.LogInformation(
                "Parsed study plan mandatory courses from class-based visualization count={Count}, codes={Codes}",
                classBasedResults.Count,
                string.Join(",", classBasedResults.Select(item => item.CourseCode)));

            return classBasedResults;
        }

        var results = new List<ParsedProgrammeCourse>();
        var headingMatches = StrongParagraphRegex.Matches(html);

        for (var i = 0; i < headingMatches.Count; i++)
        {
            var headingMatch = headingMatches[i];
            var heading = NormalizeText(headingMatch.Groups["heading"].Value);
            var bucket = MapBucket(heading);

            if (bucket is null)
            {
                continue;
            }

            var start = headingMatch.Index + headingMatch.Length;
            var end = i + 1 < headingMatches.Count ? headingMatches[i + 1].Index : html.Length;
            var sectionHtml = html[start..end];

            foreach (Match rowMatch in TableRowRegex.Matches(sectionHtml))
            {
                var rowHtml = rowMatch.Groups["row"].Value;
                var codeMatch = CourseCodeRegex.Match(rowHtml);
                if (!codeMatch.Success)
                {
                    continue;
                }

                var cells = TableCellRegex.Matches(rowHtml)
                    .Select(match => NormalizeText(match.Groups["cell"].Value))
                    .Where(cell => !string.IsNullOrWhiteSpace(cell))
                    .ToList();

                if (cells.Count < 3)
                {
                    continue;
                }

                var courseCode = codeMatch.Groups["code"].Value;
                var title = cells.Count > 1 ? cells[1] : courseCode;
                var ects = TryParseEcts(cells.Count > 2 ? cells[2] : null);

                results.Add(new ParsedProgrammeCourse
                {
                    CourseCode = courseCode,
                    Title = title,
                    Ects = ects,
                    Bucket = bucket.Value.Key,
                    BucketLabel = heading,
                    SourceOrder = results.Count + 1
                });
            }
        }

        _logger.LogInformation(
            "Parsed study plan mandatory courses count={Count}, codes={Codes}",
            results.Count,
            string.Join(",", results.Select(item => item.CourseCode)));

        return results
            .GroupBy(item => item.CourseCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.SourceOrder)
            .ToList();
    }

    private static List<ParsedProgrammeCourse> ParseMandatoryCoursesFromClassBasedStudyPlan(string html)
    {
        var results = new List<ParsedProgrammeCourse>();

        foreach (Match semesterMatch in SemesterTermRegex.Matches(html))
        {
            if (!int.TryParse(semesterMatch.Groups["semester"].Value, out _))
            {
                continue;
            }

            var itemsHtml = semesterMatch.Groups["items"].Value;
            foreach (Match itemMatch in ItemBlockRegex.Matches(itemsHtml))
            {
                var itemHtml = itemMatch.Groups["item"].Value;
                var codeMatch = CourseCodeRegex.Match(itemHtml);
                if (!codeMatch.Success)
                {
                    continue;
                }

                var bucket = MapClassBasedBucket(itemMatch.Groups["bucket"].Value);
                if (bucket is null || !IsMandatoryProgrammeBucket(bucket.Value.Key))
                {
                    continue;
                }

                var courseCode = codeMatch.Groups["code"].Value;
                var title = ExtractItemTitle(itemHtml) ?? courseCode;
                var ects = TryParseEcts(ExtractItemPoints(itemHtml));

                results.Add(new ParsedProgrammeCourse
                {
                    CourseCode = courseCode,
                    Title = title,
                    Ects = ects,
                    Bucket = bucket.Value.Key,
                    BucketLabel = bucket.Value.Value,
                    SourceOrder = results.Count + 1
                });
            }
        }

        return results
            .GroupBy(item => item.CourseCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.SourceOrder)
            .ToList();
    }

    private static KeyValuePair<string, string>? MapBucket(string heading)
    {
        var normalized = heading.ToLowerInvariant();

        if (normalized.Contains("polyteknisk"))
        {
            return new KeyValuePair<string, string>("polytechnicalFoundation", heading);
        }

        if (normalized.Contains("retningsspecifik") || normalized.Contains("programspecifik"))
        {
            return new KeyValuePair<string, string>("programmeSpecific", heading);
        }

        if (normalized.Contains("projekt"))
        {
            return new KeyValuePair<string, string>("projects", heading);
        }

        return null;
    }

    private static KeyValuePair<string, string>? MapClassBasedBucket(string rawBucket)
    {
        return MapStudyFlowBucket(rawBucket) switch
        {
            "polytechnicalFoundation" => new KeyValuePair<string, string>("polytechnicalFoundation", "Polyteknisk grundlag"),
            "programmeSpecific" => new KeyValuePair<string, string>("programmeSpecific", "Retningsspecifikke kurser"),
            "projects" => new KeyValuePair<string, string>("projects", "Projekter"),
            "electives" => new KeyValuePair<string, string>("electives", "Valgfrie kurser"),
            _ => null
        };
    }

    private static string NormalizeText(string htmlFragment)
    {
        var withoutTags = Regex.Replace(htmlFragment, "<.*?>", " ");
        return WebUtility.HtmlDecode(withoutTags)
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Trim();
    }

    private static double? TryParseEcts(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Replace(",", ".", StringComparison.Ordinal).Trim();
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var ects)
            ? ects
            : null;
    }

    private static CourseSummary BuildMergedCourseSummary(
        ParsedProgrammeCourse parsedCourse,
        IReadOnlyDictionary<string, CourseSummary> courseLookup)
    {
        if (courseLookup.TryGetValue(parsedCourse.CourseCode, out var enrichedCourse))
        {
            return enrichedCourse;
        }

        return new CourseSummary
        {
            CourseCode = parsedCourse.CourseCode,
            Title = parsedCourse.Title,
            CourseLevel = null,
            Ects = parsedCourse.Ects
        };
    }

    private static ProgrammeBucketLimits? ResolveBucketLimits(string level)
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

    private static VisualizationItem? FindVisualization(IEnumerable<VisualizationItem> visualizations, string name)
    {
        return visualizations.FirstOrDefault(visualization =>
            string.Equals(visualization.NameDanish, name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(visualization.NameEnglish, name, StringComparison.OrdinalIgnoreCase));
    }

    private static List<ProgrammeVisualizationReference> BuildVisualizationReferences(params VisualizationItem?[] visualizations)
    {
        return visualizations
            .Where(visualization => visualization is not null)
            .Select(visualization => ToReference(visualization!))
            .ToList();
    }

    private static ProgrammeVisualizationReference ToReference(VisualizationItem visualization)
    {
        return new ProgrammeVisualizationReference
        {
            Name = visualization.NameDanish ?? visualization.NameEnglish ?? visualization.Id.ToString(CultureInfo.InvariantCulture),
            Id = visualization.Id,
            Guid = visualization.Guid
        };
    }

    private static List<string> BuildProgrammeDefinitionNotes(
        string code,
        int resolvedVolume,
        int? studyBoxSourceVolume,
        bool hasStudyPlanView,
        bool hasStudyFlowView,
        int mandatoryCourseCount,
        int studyFlowOptionCount)
    {
        var notes = new List<string>
        {
            "This is the generic programme-definition contract intended for the future frontend programme dropdown.",
            studyBoxSourceVolume is not null
                ? $"Mandatory programme bucket classification is resolved from strict Courseblock StudyBox labels in volume {studyBoxSourceVolume.Value} and enriched through the course catalog service."
                : "Mandatory courses fall back to Studieplan visualization parsing when Courseblock StudyBox labels are unavailable.",
            "Electives are treated as the residual category rather than as explicit mandatory-course entries.",
            $"Programme metadata and visualization mappings were resolved from volume {resolvedVolume}."
        };

        if (hasStudyFlowView)
        {
            notes.Add("A matching Studieforløb visualization was resolved and can later be used for recommended semester placement.");
        }
        else
        {
            notes.Add("No Studieforløb visualization was resolved for this programme, so recommended semester placement is not yet attached.");
        }

        if (!hasStudyPlanView || mandatoryCourseCount == 0)
        {
            notes.Add("No mandatory-course rows were parsed from Studieplan for this programme yet.");
        }

        if (studyFlowOptionCount > 0)
        {
            notes.Add("Study-flow dropdown options were generated from the programme's Studieforløb and recommended package visualizations.");
        }

        if (string.Equals(code, "ELEKTEK23", StringComparison.OrdinalIgnoreCase))
        {
            notes.Add("ELEKTEK23 is the first fully wired example behind this generic shape, including recommended package-view discovery and the curated generic-plan preset.");
        }

        return notes;
    }

    private sealed class ParsedProgrammeCourse
    {
        public string CourseCode { get; init; } = "";
        public string Title { get; init; } = "";
        public double? Ects { get; init; }
        public string Bucket { get; init; } = "";
        public string BucketLabel { get; init; } = "";
        public int SourceOrder { get; init; }
        public int Priority { get; init; }
    }

    private sealed record ProgrammeStudyBoxClassification
    {
        public List<ParsedProgrammeCourse> MandatoryCourses { get; init; } = [];
        public List<string> ApprovedMscElectiveCourseCodes { get; init; } = [];
        public int? SourceVolume { get; init; }
        public bool HasData => MandatoryCourses.Count > 0 || ApprovedMscElectiveCourseCodes.Count > 0;
    }

    private sealed class ParsedStudyFlowPlan
    {
        public List<ParsedStudyFlowPlacement> CoursePlacements { get; init; } = [];
    }

    private sealed class ParsedStudyFlowPlacement
    {
        public string CourseCode { get; init; } = "";
        public string? Title { get; set; }
        public double? Ects { get; set; }
        public string? Bucket { get; set; }
        public int Semester { get; init; }
        public int SourceOrder { get; init; }
        public List<string> TimeBlocks { get; set; } = [];
    }
}
