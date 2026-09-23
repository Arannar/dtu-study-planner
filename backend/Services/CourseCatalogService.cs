using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Extensions.Options;
using Planner.Backend.Models;

namespace Planner.Backend.Services;

public interface ICourseCatalogService
{
    Task<CoursesResponse> SearchCoursesAsync(int volume, string? query);
    Task<CoursesResponse> GetCoursesForStudyPlanAsync(int volume, IEnumerable<string> courseCodes, bool allowHistoricalFallback = false);
}

public sealed class CourseCatalogService(IDtuGateway gateway, DtuCache cache, IOptions<DtuOptions> options,
    ILogger<CourseCatalogService> logger) : ICourseCatalogService
{
    private sealed record CachedCourse(CourseSummary? Course);

    public async Task<CoursesResponse> SearchCoursesAsync(int volume, string? query)
    {
        var search = query?.Trim();
        if (string.IsNullOrEmpty(search)) throw new ArgumentException("Enter a course number, title or description.");
        var year = new AcademicYear(volume);
        var isCode = Regex.IsMatch(search, @"^[0-9]{5}$");
        var xml = await gateway.SearchCoursesAsync(year, isCode ? search : "", isCode ? "" : search);
        return new CoursesResponse
        {
            Courses = xml.SelectNodes("descendant-or-self::*[local-name()='Course']")!
                .OfType<XmlElement>()
                .Where(course => course.GetAttribute("Volume") == year.Catalogue &&
                    (!isCode || course.GetAttribute("CourseCode") == search))
                .Select(CourseXmlParser.Parse)
                .DistinctBy(course => course.CourseCode)
                .OrderBy(course => course.CourseCode, StringComparer.Ordinal)
                .ToList()
        };
    }

    public Task<CoursesResponse> GetCoursesForStudyPlanAsync(int volume, IEnumerable<string> courseCodes, bool allowHistoricalFallback = false)
    {
        var year = new AcademicYear(volume);
        var codes = courseCodes.Where(code => !string.IsNullOrWhiteSpace(code)).Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (codes.Any(code => !Regex.IsMatch(code, @"^\d{5}$")))
            throw new ArgumentException("Course codes must contain exactly five digits.");
        if (codes.Length > 500) throw new ArgumentException("Request at most 500 courses at once.");

        // Serialize cache fills per volume, including overlapping batches from concurrent requests.
        return cache.WithLockAsync($"course-fill:{year.Catalogue}", async () =>
        {
            string Key(string code) => $"course:{year.Catalogue}:{code}:{allowHistoricalFallback}";
            var resolved = new Dictionary<string, CachedCourse>();
            foreach (var code in codes)
                if (cache.TryGet<CachedCourse>(Key(code), out var cached)) resolved[code] = cached!;
            var pending = codes.Where(code => !resolved.ContainsKey(code)).ToArray();
            logger.LogInformation("Course batch volume={Volume} requested={Count} cached={Hits}", year.Catalogue, codes.Length, codes.Length - pending.Length);

            foreach (var chunk in pending.Chunk(options.Value.CourseBatchSize))
            {
                var xml = await gateway.GetCoursesAsync(year, chunk);
                var requested = chunk.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var courses = xml.SelectNodes("descendant-or-self::*[local-name()='Course']")!
                    .OfType<XmlElement>()
                    .Where(course => requested.Contains(course.GetAttribute("CourseCode")) &&
                        course.GetAttribute("Volume") == year.Catalogue)
                    .Select(CourseXmlParser.Parse)
                    .GroupBy(course => course.CourseCode).ToDictionary(group => group.Key, group => group.First());
                if (allowHistoricalFallback && courses.Count == 0)
                {
                    foreach (var group in chunk.Chunk(4))
                    {
                        var historicalCourses = await Task.WhenAll(group.Select(async code =>
                        {
                            var historicalXml = await gateway.GetCourseAsync(year, code);
                            var course = historicalXml.SelectNodes("descendant-or-self::*[local-name()='Course']")!
                                .OfType<XmlElement>()
                                .FirstOrDefault(node => node.GetAttribute("CourseCode") == code &&
                                    node.GetAttribute("Volume") == year.Catalogue);
                            return course is null ? null : CourseXmlParser.Parse(course);
                        }));
                        foreach (var course in historicalCourses.OfType<CourseSummary>())
                            courses[course.CourseCode] = course;
                    }
                }
                foreach (var code in chunk)
                {
                    var item = new CachedCourse(courses.GetValueOrDefault(code));
                    resolved[code] = item;
                    cache.Set(Key(code), item, item.Course is null ? TimeSpan.FromMinutes(options.Value.MissingCacheMinutes) : null);
                }
            }

            return new CoursesResponse
            {
                Courses = codes.Where(code => resolved[code].Course is not null).Select(code => resolved[code].Course!).ToList(),
                MissingCourseCodes = codes.Where(code => resolved[code].Course is null).ToList()
            };
        });
    }
}
