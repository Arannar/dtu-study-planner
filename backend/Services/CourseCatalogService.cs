// CourseCatalogService
// → calls GetDataForStudyplan(volume, courseCodes) on the course database SOAP service
// → maps SOAP types into your own CourseSummary

using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using Planner.Backend.Models;

namespace Planner.Backend.Services;

public interface ICourseCatalogService
{
    Task<CoursesResponse> GetCoursesForStudyPlanAsync(int volume, IEnumerable<string> courseCodes);
}

public sealed class CourseCatalogService : ICourseCatalogService
{
    private readonly CourseSoapClient _client;
    private readonly ILogger<CourseCatalogService> _logger;

    public CourseCatalogService(
        CourseSoapClient client,
        ILogger<CourseCatalogService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<CoursesResponse> GetCoursesForStudyPlanAsync(int volume, IEnumerable<string> courseCodes)
    {
        var codeList = (courseCodes ?? Enumerable.Empty<string>())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _logger.LogInformation(
            "Building courses with volume={Volume}, codeCount={Count}, codes={Codes}",
            volume,
            codeList.Length,
            string.Join(",", codeList));

        var results = new List<CourseSummary>();
        var missingCourseCodes = new List<string>();
        var volumeText = volume.ToString(CultureInfo.InvariantCulture);

        foreach (var courseCode in codeList)
        {
            string? title = null;
            string? courseLevel = null;
            double? ects = null;
            string? scheduleText = null;
            string gradingMode = "unknown";
            string examinerMode = "unknown";
            var timeBlocks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rawKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var xml = await _client.GetCourseNewestBeforeVolumeAsync(courseCode, volumeText);
                if (xml != null)
                {
                    var parsed = ParseCourseXml(xml);
                    title ??= parsed.Title;
                    courseLevel ??= parsed.CourseLevel;
                    ects ??= parsed.Ects;
                    scheduleText ??= parsed.ScheduleText;
                    gradingMode = PreferKnownMode(gradingMode, parsed.GradingMode);
                    examinerMode = PreferKnownMode(examinerMode, parsed.ExaminerMode);

                    foreach (var block in parsed.TimeBlocks)
                        timeBlocks.Add(block);
                    
                    foreach (var raw in parsed.RawScheduleKeys)
                        rawKeys.Add(raw);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "GetCourseNewestBeforeVolumeAsync failed for {CourseCode}, volume={Volume}",
                    courseCode,
                    volumeText);
            }

            try
            {
                var xml = await _client.GetCourseHoldingsAsync(courseCode, volumeText, "en-GB");
                if (xml != null)
                {
                    var parsed = ParseCourseXml(xml);
                    title ??= parsed.Title;
                    courseLevel ??= parsed.CourseLevel;
                    ects ??= parsed.Ects;
                    scheduleText ??= parsed.ScheduleText;
                    gradingMode = PreferKnownMode(gradingMode, parsed.GradingMode);
                    examinerMode = PreferKnownMode(examinerMode, parsed.ExaminerMode);

                    foreach (var block in parsed.TimeBlocks)
                        timeBlocks.Add(block);

                    foreach (var raw in parsed.RawScheduleKeys)
                        rawKeys.Add(raw);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "GetCourseHoldingsAsync failed for {CourseCode}, volume={Volume}",
                    courseCode,
                    volumeText);
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                try
                {
                    var titles = await _client.GetCoursesTitlesAsync(volumeText, new[] { courseCode });
                    var t = titles?.FirstOrDefault();
                    title = t?.EnglishTitle ?? t?.DanishTitle;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "GetCoursesTitlesAsync failed for {CourseCode}, volume={Volume}",
                        courseCode,
                        volumeText);
                }
            }

            var hasCourseData =
                !string.IsNullOrWhiteSpace(title) ||
                ects.HasValue ||
                !string.IsNullOrWhiteSpace(scheduleText) ||
                timeBlocks.Count > 0 ||
                rawKeys.Count > 0;

            if (!hasCourseData)
            {
                missingCourseCodes.Add(courseCode);
                _logger.LogInformation(
                    "Course code {CourseCode} was not found for volume={Volume}",
                    courseCode,
                    volumeText);
                continue;
            }

            var placementOptions = ParsePlacementOptions(scheduleText);
            var scheduleTextTimeBlocks = ParseExplicitScheduleTextTimeBlocks(scheduleText);
            var selectedPlacementOptionId = placementOptions.FirstOrDefault()?.Id;
            var effectiveTimeBlocks = placementOptions.Count > 0
                ? placementOptions[0].TimeBlocks
                : timeBlocks.Count > 0
                    ? timeBlocks.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()
                    : scheduleTextTimeBlocks;

            results.Add(new CourseSummary
            {
                CourseCode = courseCode,
                Title = title ?? courseCode,
                CourseLevel = courseLevel,
                Ects = ects,
                ScheduleText = scheduleText,
                GradingMode = gradingMode,
                ExaminerMode = examinerMode,
                TimeBlocks = effectiveTimeBlocks,
                SelectedPlacementOptionId = selectedPlacementOptionId,
                PlacementOptions = placementOptions,
                RawScheduleKeys = rawKeys.OrderBy(x => x).ToList()
            });
        }

        return new CoursesResponse
        {
            Courses = results,
            MissingCourseCodes = missingCourseCodes
        };
    }

    private static ParsedCourseData ParseCourseXml(XmlNode xml)
    {
        var course = FindFirstElement(xml, "Course");
        if (course == null)
        {
            return new ParsedCourseData();
        }

        var teachingBlocks = ExtractTeachingTimeBlocks(course);
        var examMetadata = ExtractExamMetadata(course);

        return new ParsedCourseData
        {
            Title = ExtractTitle(course),
            CourseLevel = ExtractCourseLevel(course),
            Ects = ExtractEcts(course),
            ScheduleText = ExtractScheduleText(course),
            GradingMode = examMetadata.GradingMode,
            ExaminerMode = examMetadata.ExaminerMode,
            TimeBlocks = teachingBlocks.Normalized,
            RawScheduleKeys = teachingBlocks.Raw
        };
    }

    private static string PreferKnownMode(string current, string candidate)
    {
        return string.Equals(current, "unknown", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(candidate, "unknown", StringComparison.OrdinalIgnoreCase)
                ? candidate
                : current;
    }

    private static string? ExtractCourseLevel(XmlElement course)
    {
        var level = course
            .GetElementsByTagName("CBS_Programme_Level")
            .OfType<XmlElement>()
            .FirstOrDefault();

        var raw = level?.GetAttribute("CBS_Programme_LevellKey")?.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return raw.ToUpperInvariant() switch
        {
            "DTU_BSC" => "bsc",
            "DTU_MSC" => "msc",
            "DTU_BENG" => "beng",
            _ => raw.ToLowerInvariant()
        };
    }

    private static string? ExtractTitle(XmlElement course)
    {
        return FindLocalizedAttributeValue(
            parent: course,
            elementName: "Title",
            langAttributeName: "Lang",
            valueAttributeName: "Title",
            preferredLang: "en-GB");
    }

    private static double? ExtractEcts(XmlElement course)
    {
        var point = course
            .GetElementsByTagName("Point")
            .OfType<XmlElement>()
            .FirstOrDefault(e =>
                string.Equals(e.GetAttribute("PointType"), "ECTS", StringComparison.OrdinalIgnoreCase));

        if (point == null)
            return null;

        var raw = point.InnerText?.Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        raw = raw.Replace(",", ".");

        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static string? ExtractScheduleText(XmlElement course)
    {
        var classSchedules = course
            .GetElementsByTagName("Class_Schedule")
            .OfType<XmlElement>()
            .ToList();

        foreach (var classSchedule in classSchedules)
        {
            var text = FindLocalizedAttributeValue(
                parent: classSchedule,
                elementName: "Schedule_Txt",
                langAttributeName: "Lang",
                valueAttributeName: "Txt",
                preferredLang: "en-GB");

            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        return null;
    }

    private static (List<string> Raw, List<string> Normalized) ExtractTeachingTimeBlocks(XmlElement course)
    {
        var raw = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var classSchedules = course
            .GetElementsByTagName("Class_Schedule")
            .OfType<XmlElement>();

        foreach (var classSchedule in classSchedules)
        {
            foreach (var schedule in classSchedule.GetElementsByTagName("Schedule").OfType<XmlElement>())
            {
                var rawKey = schedule.GetAttribute("ScheduleKey")?.Trim();

                if (string.IsNullOrWhiteSpace(rawKey))
                    continue;

                raw.Add(rawKey);

                foreach (var n in NormalizeTeachingBlock(rawKey))
                    normalized.Add(n);
            }
        }

        return (
            raw.OrderBy(x => x).ToList(),
            normalized.OrderBy(x => x).ToList()
        );
    }

    private static (string GradingMode, string ExaminerMode) ExtractExamMetadata(XmlElement course)
    {
        var examinations = course
            .GetElementsByTagName("Examination")
            .OfType<XmlElement>()
            .ToList();

        var examination = examinations
            .Where(IsVisible)
            .OrderBy(GetExaminationSortId)
            .FirstOrDefault()
            ?? examinations.OrderBy(GetExaminationSortId).FirstOrDefault();

        if (examination is null)
        {
            return ("unknown", "unknown");
        }

        var markingScaleKey = examination
            .GetElementsByTagName("Marking_Scale")
            .OfType<XmlElement>()
            .FirstOrDefault()
            ?.GetAttribute("Marking_ScaleKey")
            ?.Trim();
        var evaluationKey = examination
            .GetElementsByTagName("Evaluation")
            .OfType<XmlElement>()
            .FirstOrDefault()
            ?.GetAttribute("EvaluationKey")
            ?.Trim();

        return (MapGradingMode(markingScaleKey), MapExaminerMode(evaluationKey));
    }

    private static bool IsVisible(XmlElement element)
    {
        var show = element.GetAttribute("Show")?.Trim();
        return string.IsNullOrWhiteSpace(show) || bool.TryParse(show, out var visible) && visible;
    }

    private static int GetExaminationSortId(XmlElement examination)
    {
        var raw = examination.GetAttribute("ExaminationSortID")?.Trim();
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sortId)
            ? sortId
            : int.MaxValue;
    }

    private static string MapGradingMode(string? markingScaleKey)
    {
        return markingScaleKey?.ToUpperInvariant() switch
        {
            "GLOB_BESTAEET" => "passFail",
            "GLOB_7SKALA" => "graded",
            _ => "unknown"
        };
    }

    private static string MapExaminerMode(string? evaluationKey)
    {
        return evaluationKey?.ToUpperInvariant() switch
        {
            "GLOB_EXTERN" => "external",
            "GLOB_INTERN" => "internal",
            _ => "unknown"
        };
    }

    private static IEnumerable<string> NormalizeTeachingBlock(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            yield break;

        var key = value.Trim();

        if (IsMonthIntensiveBlock(key))
        {
            yield return NormalizeMonthBlock(key);
            yield break;
        }

        var normalized = key.ToUpperInvariant();

        if (!IsWeeklyTeachingBlock(normalized))
            yield break;

        // E1, F2, E5 etc. => whole-day recurring weekly module, expand to A + B
        if (normalized.Length == 2 && normalized[1] is >= '1' and <= '5')
        {
            yield return normalized + "A";
            yield return normalized + "B";
            yield break;
        }

        // E7 / F7 => evening module, atomic as-is
        if (normalized.Length == 2 && normalized[1] == '7')
        {
            yield return normalized;
            yield break;
        }

        // Already atomic, e.g. E3A or F5B
        yield return normalized;
    }

    private static List<CoursePlacementOption> ParsePlacementOptions(string? scheduleText)
    {
        if (string.IsNullOrWhiteSpace(scheduleText))
            return new List<CoursePlacementOption>();

        var matches = Regex.Matches(
            scheduleText,
            @"(?:Scheme|Skema)\s*([A-Z])\s*:\s*(.*?)(?=(?:,|\s)*(?:Scheme|Skema)\s*[A-Z]\s*:|$)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        if (matches.Count < 2)
            return new List<CoursePlacementOption>();

        var options = new List<CoursePlacementOption>();

        foreach (Match match in matches)
        {
            var id = match.Groups[1].Value.Trim().ToUpperInvariant();
            var body = match.Groups[2].Value;
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(body))
                continue;

            var blocks = Regex.Matches(
                    body,
                    @"\b(?:[EF](?:[1-5](?:[AB])?|7)|January|June|July|August)\b",
                    RegexOptions.IgnoreCase)
                .Select(token => token.Value)
                .SelectMany(NormalizeTeachingBlock)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(block => block, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (blocks.Count == 0)
                continue;

            options.Add(new CoursePlacementOption
            {
                Id = id,
                Label = $"Scheme {id}",
                TimeBlocks = blocks
            });
        }

        return options;
    }

    private static List<string> ParseExplicitScheduleTextTimeBlocks(string? scheduleText)
    {
        if (string.IsNullOrWhiteSpace(scheduleText))
            return new List<string>();

        return Regex.Matches(
                scheduleText,
                @"\b(?:[EF](?:[1-5](?:[AB])?|7)|January|June|July|August)\b",
                RegexOptions.IgnoreCase)
            .Select(match => match.Value)
            .SelectMany(NormalizeTeachingBlock)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(block => block, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsWeeklyTeachingBlock(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return System.Text.RegularExpressions.Regex.IsMatch(
            value.Trim(),
            @"^[EF](?:[1-5](?:[AB])?|7)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static bool IsMonthIntensiveBlock(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.Equals("January", StringComparison.OrdinalIgnoreCase)
            || value.Equals("June", StringComparison.OrdinalIgnoreCase)
            || value.Equals("July", StringComparison.OrdinalIgnoreCase)
            || value.Equals("August", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeMonthBlock(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "january" => "JANUARY",
            "june" => "JUNE",
            "july" => "JULY",
            "august" => "AUGUST",
            _ => value.Trim().ToUpperInvariant()
        };
    }
    private static XmlElement? FindFirstElement(XmlNode root, string name)
    {
        if (root is XmlElement rootElement && string.Equals(rootElement.Name, name, StringComparison.OrdinalIgnoreCase))
            return rootElement;

        return root
            .ChildNodes
            .OfType<XmlNode>()
            .SelectMany(DescendantsAndSelf)
            .OfType<XmlElement>()
            .FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<XmlNode> DescendantsAndSelf(XmlNode node)
    {
        yield return node;

        foreach (XmlNode child in node.ChildNodes)
        {
            foreach (var descendant in DescendantsAndSelf(child))
                yield return descendant;
        }
    }

    private static string? FindLocalizedAttributeValue(
        XmlElement parent,
        string elementName,
        string langAttributeName,
        string valueAttributeName,
        string preferredLang)
    {
        var matches = parent
            .GetElementsByTagName(elementName)
            .OfType<XmlElement>()
            .ToList();

        var preferred = matches.FirstOrDefault(e =>
            string.Equals(e.GetAttribute(langAttributeName), preferredLang, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(e.GetAttribute(valueAttributeName)));

        if (preferred != null)
            return preferred.GetAttribute(valueAttributeName).Trim();

        var englishFallback = matches.FirstOrDefault(e =>
            string.Equals(e.GetAttribute(langAttributeName), "en-GB", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(e.GetAttribute(valueAttributeName)));

        if (englishFallback != null)
            return englishFallback.GetAttribute(valueAttributeName).Trim();

        var danishFallback = matches.FirstOrDefault(e =>
            string.Equals(e.GetAttribute(langAttributeName), "da-DK", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(e.GetAttribute(valueAttributeName)));

        if (danishFallback != null)
            return danishFallback.GetAttribute(valueAttributeName).Trim();

        var any = matches.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.GetAttribute(valueAttributeName)));
        return any?.GetAttribute(valueAttributeName).Trim();
    }

    private sealed class ParsedCourseData
    {
        public string? Title { get; init; }
        public string? CourseLevel { get; init; }
        public double? Ects { get; init; }
        public string? ScheduleText { get; init; }
        public string GradingMode { get; init; } = "unknown";
        public string ExaminerMode { get; init; } = "unknown";
        public List<string> TimeBlocks { get; init; } = new();
        public List<string> RawScheduleKeys { get; init; } = new();
    }
}

