using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Planner.Backend.Services;

// Parses supplied markup only: no browser, scripts, network access, or course enrichment.
public static class StudyFlowParser
{
    public static ParsedStudyFlowPlan Parse(string html)
    {
        using var document = new HtmlParser().ParseDocument(html);
        var placements = new Dictionary<(string Code, int Semester), ParsedStudyFlowPlacement>();
        ParsedStudyFlowPlacement Get(string code, int semester)
        {
            var key = (code, semester);
            if (!placements.TryGetValue(key, out var placement))
            {
                placement = new ParsedStudyFlowPlacement { CourseCode = code, Semester = semester, SourceOrder = placements.Count };
                placements.Add(key, placement);
            }
            return placement;
        }

        foreach (var term in document.QuerySelectorAll(".term"))
        {
            var semester = SemesterNumber(term.QuerySelector(".header")?.TextContent);
            if (semester is null) continue;
            foreach (var item in term.QuerySelectorAll(".item"))
            {
                var code = CourseCode(item);
                if (code is null) continue;
                var placement = Get(code, semester.Value);
                placement.Title ??= item.QuerySelector("strong, .title")?.TextContent.Trim();
                placement.Ects ??= Ects(item.QuerySelector(".point")?.TextContent);
                placement.Bucket ??= MapBucket(string.Join(' ', item.ClassList));
            }
        }

        // Timetables and supplementary tables follow a semester heading. Traversing the DOM
        // tolerates extra wrappers, reordered attributes/classes and both quote styles.
        int? currentSemester = null;
        foreach (var element in document.All)
        {
            if (element.LocalName == "p" && element.QuerySelector("strong") is not null)
            {
                var number = SemesterNumber(element.TextContent);
                if (number is not null) currentSemester = number;
            }
            if (currentSemester is null) continue;
            if (element.ClassList.Contains("dayinfo"))
            {
                var period = new[] { "early", "late", "night" }.FirstOrDefault(element.ClassList.Contains);
                var day = new[] { "monday", "tuesday", "wednesday", "thursday", "friday" }.FirstOrDefault(element.ClassList.Contains);
                var block = ToSemesterBlock(currentSemester.Value, day, period);
                if (block is null) continue;
                foreach (var link in element.QuerySelectorAll("a[href]"))
                {
                    var code = CourseCode(link);
                    if (code is not null) Get(code, currentSemester.Value).TimeBlocks.Add(block);
                }
            }
            if (element.LocalName == "tr")
            {
                var code = CourseCode(element);
                if (code is null) continue;
                var placement = Get(code, currentSemester.Value);
                placement.Title ??= element.QuerySelector("strong, .title")?.TextContent.Trim();
                placement.Ects ??= Ects(element.QuerySelector(".point")?.TextContent);
                placement.TimeBlocks.AddRange(ParseBlocks(ElementText(element)));
            }
        }

        foreach (var placement in placements.Values)
            placement.TimeBlocks = placement.TimeBlocks.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(block => block).ToList();
        return new ParsedStudyFlowPlan { CoursePlacements = placements.Values.OrderBy(p => p.Semester).ThenBy(p => p.SourceOrder).ToList() };
    }

    private static int? SemesterNumber(string? text)
    {
        var match = Regex.Match(text ?? "", @"^\s*(\d+)\.\s*sem(?:ester|ster)?\s*$", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out var value) && value is >= 1 and <= 12 ? value : null;
    }

    private static string? CourseCode(IElement element)
    {
        var href = element.GetAttribute("href") ?? element.QuerySelector("a[href*='/course/']")?.GetAttribute("href");
        var match = Regex.Match(href ?? "", @"/course/(?:\d{4}[-/]\d{4}/)?(\d{5})(?:\b|/)");
        if (!match.Success) match = Regex.Match(ElementText(element), @"\b(\d{5})\b");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string ElementText(IElement element) => element.LocalName == "tr"
        ? string.Join(' ', element.QuerySelectorAll("td, th").Select(cell => cell.TextContent))
        : element.TextContent;

    private static double? Ects(string? value)
    {
        var match = Regex.Match(value ?? "", @"\d+(?:[.,]\d+)?");
        return double.TryParse(match.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var ects) ? ects : null;
    }

    private static string? MapBucket(string value) => value.ToLowerInvariant() switch
    {
        var s when s.Contains("polytechnic") => "polytechnicalFoundation",
        var s when s.Contains("programmespecific") => "programmeSpecific",
        var s when s.Contains("project") => "projects",
        var s when s.Contains("elective") => "electives",
        _ => null
    };

    private static IEnumerable<string> ParseBlocks(string text)
    {
        foreach (Match match in Regex.Matches(text, @"\b([EF](?:[1-5][AB]?|7)|January|June|July|August|Januar|Juni|Juli)\b", RegexOptions.IgnoreCase))
        {
            var block = match.Value.ToUpperInvariant() switch { "JANUAR" => "JANUARY", "JUNI" => "JUNE", "JULI" => "JULY", var s => s };
            if (block.Length == 2 && block[1] is >= '1' and <= '5')
            {
                yield return block + "A";
                yield return block + "B";
            }
            else yield return block;
        }
    }

    private static string? ToSemesterBlock(int semester, string? day, string? period)
    {
        var block = (day, period) switch
        {
            ("monday", "early") => "1A", ("tuesday", "early") => "3A", ("wednesday", "early") => "5A",
            ("thursday", "early") => "2B", ("friday", "early") => "4B",
            ("monday", "late") => "2A", ("tuesday", "late") => "4A", ("wednesday", "late") => "5B",
            ("thursday", "late") => "1B", ("friday", "late") => "3B",
            (_, "night") => "7", _ => null
        };
        return block is null ? null : (semester % 2 == 1 ? "E" : "F") + block;
    }
}

public sealed class ParsedStudyFlowPlan
{
    public List<ParsedStudyFlowPlacement> CoursePlacements { get; init; } = [];
}

public sealed class ParsedStudyFlowPlacement
{
    public string CourseCode { get; init; } = "";
    public string? Title { get; set; }
    public double? Ects { get; set; }
    public string? Bucket { get; set; }
    public int Semester { get; init; }
    public int SourceOrder { get; init; }
    public List<string> TimeBlocks { get; set; } = [];
}
