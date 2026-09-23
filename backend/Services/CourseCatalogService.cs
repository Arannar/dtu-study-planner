using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Extensions.Options;
using Planner.Backend.Models;

namespace Planner.Backend.Services;

public interface ICourseCatalogService
{
    Task<CoursesResponse> GetCoursesForStudyPlanAsync(int volume, IEnumerable<string> courseCodes);
}

public sealed class CourseCatalogService(IDtuGateway gateway, DtuCache cache, IOptions<DtuOptions> options,
    ILogger<CourseCatalogService> logger) : ICourseCatalogService
{
    private sealed record CachedCourse(CourseSummary? Course);

    public Task<CoursesResponse> GetCoursesForStudyPlanAsync(int volume, IEnumerable<string> courseCodes)
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
            string Key(string code) => $"course:{year.Catalogue}:{code}";
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
