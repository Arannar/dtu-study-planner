using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Planner.Backend.Models;
using Planner.Backend.Services;
using Planner.Backend.Soap.Courseblocks;
using Planner.Backend.Soap.Visualizations;
using Planner.Backend.Soap.Volumes;
using static CatalogTests;

internal static class ProgrammeTests
{
    public static async Task Run()
    {
        var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "study-flow-5302.html"));
        var parsed = StudyFlowParser.Parse(html);
        Require(parsed.CoursePlacements.Count > 15, "Live package fixture must produce semester placements");
        Require(parsed.CoursePlacements.Any(p => p.CourseCode == "01001" && p.Semester == 1 && p.Ects == 10 && p.Bucket == "polytechnicalFoundation"), "Card metadata was lost");
        Require(parsed.CoursePlacements.Count(p => p.CourseCode == "10060") == 2, "Multi-semester course must retain both placements");
        var modified = html.Replace("class=\"term\"", "data-extra='x' class='term extra'").Replace("class=\"header\"", "class='header' data-test='1'");
        Require(StudyFlowParser.Parse(modified).CoursePlacements.Count == parsed.CoursePlacements.Count, "Attribute and quoting changes must not break parsing");
        Require(StudyFlowParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "study-flow-4826.html"))).CoursePlacements.Count == 0,
            "A course classification table must not invent semester placements");
        var timetable = StudyFlowParser.Parse("""
            <p><strong>7. semester</strong></p><section><div class='schema'>
            <div class='monday dayinfo early'><div class='course'><a href='/course/2026-2027/01001'>01001</a></div></div>
            </div><table><tr><td>01001</td><td>January E2</td></tr></table></section>
            <p><strong>8. semester</strong></p><div class='schema'><div class='night friday dayinfo'><a href='/course/10060'>10060</a></div></div>
            """);
        Require(timetable.CoursePlacements.Single(p => p.CourseCode == "01001").TimeBlocks.SequenceEqual(new[] { "E1A", "E2A", "E2B", "JANUARY" }), "Timetable blocks, months and later semesters must be retained");
        Require(timetable.CoursePlacements.Single(p => p.CourseCode == "10060").TimeBlocks.Contains("F7"), "Evening module must be preserved");

        var gateway = new FakeDtuGateway
        {
            Html = html,
            Educations = [new Education
            {
                Id = 149, Name = new Planner.Backend.Soap.Volumes.LanguageItem { English = "Bachelor of Science" },
                Lines = [new Line { Code = "ELEKTEK23", Name = new Planner.Backend.Soap.Volumes.LanguageItem { English = "Electrical Engineering" } }]
            }],
            StudyBoxes = [new StudyBoxInfo { English = "Polytechnical foundation (BSc), Electrical Engineering", CourseCodes = ["01001"] }],
            Views = [new Visualization { Id = 5302, Name = new Planner.Backend.Soap.Visualizations.LanguageItem { Danish = "EL_Atuo_23" } }],
            Volumes = [new Volume { Year = 2026, Active = true, Current = true }]
        };
        var settings = Options.Create(new DtuOptions());
        using var cache = new DtuCache(settings);
        var catalogue = new CourseCatalogService(gateway, cache, settings, NullLogger<CourseCatalogService>.Instance);
        var classifications = new ProgrammeClassificationService(gateway, NullLogger<ProgrammeClassificationService>.Instance);
        var service = new ProgrammeService(gateway, catalogue,
            new ProgrammeVisualizationService(gateway, NullLogger<ProgrammeVisualizationService>.Instance),
            new NoPreset(), classifications, cache, NullLogger<ProgrammeService>.Instance);
        var definition = await service.GetProgrammeDefinitionAsync(2026, "ELEKTEK23", "da-DK");
        Require(definition?.StudyFlowOptions.Count == 1 && definition.ClassificationVolume == 2026, "Definition must expose descriptors and classification provenance");
        Require(gateway.HtmlCalls == 0 && gateway.CourseCalls == 1, "Programme selection must not fetch or enrich package HTML");
        var option = await service.GetStudyFlowAsync(2026, "ELEKTEK23", "view-5302", "da-DK");
        Require(option is not null && gateway.HtmlCalls == 1 && gateway.CourseCalls == 2, "Import must fetch only the selected package and batch its uncached courses");
        Require(option!.SavedPlan.Plan.Courses.Any(c => c.Bucket == "polytechnicalFoundation"), "Imported courses must retain classified buckets");
        Require(option.SavedPlan.Plan.Courses.Single(c => c.CourseCode == "10060" && c.Semester % 2 == 0).Ects == 0, "Continuation ECTS must not double count");
        Require(await service.GetStudyFlowAsync(2026, "ELEKTEK23", "view-999", "da-DK") is null && gateway.HtmlCalls == 1, "Unknown option must not query arbitrary view IDs");
        var resolver = new VolumeResolver(gateway);
        Require(await resolver.ResolveAsync(null) == 2026 && await resolver.ResolveAsync(2024) == 2024, "Default volume discovery must reconcile catalogues and preserve explicit years");

        foreach (var (level, label, bucket) in new[]
        {
            ("bsc", "Programme specific course (BSc), Electrical Engineering", "programmeSpecific"),
            ("beng", "Mandatory course (B Eng), Electrical Engineering", "mandatory"),
            ("msc", "Projects (MSc), Electrical Engineering", "projects")
        })
        {
            gateway.StudyBoxes = [new StudyBoxInfo { English = label, CourseCodes = ["01001", "01001", "invalid"] },
                new StudyBoxInfo { English = label + " unrelated", CourseCodes = ["10060"] }];
            var classification = await classifications.ResolveAsync(new ProgrammeListItem { Level = level, ProgrammeNameEnglish = "Electrical Engineering" }, 2026);
            Require(classification.MandatoryCourses.Count == 1 && classification.MandatoryCourses[0].Bucket == bucket, "Strict programme classification must survive extraction");
        }

        var loads = 0;
        var snapshots = Enumerable.Range(0, 10).Select(_ => cache.GetAsync("shared-snapshot", async () => { loads++; await Task.Delay(10); return 42; }));
        await Task.WhenAll(snapshots);
        Require(loads == 1, "Shared metadata cache must coalesce concurrent calls");
    }

    private sealed class NoPreset : IGenericStudyFlowPresetLoader
    {
        public bool TryLoad(string code, out ProgrammeStudyFlowOption? option) { option = null; return false; }
    }
}
