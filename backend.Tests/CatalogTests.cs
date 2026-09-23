using System.Xml;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Planner.Backend.Services;
using Planner.Backend.Soap.Courseblocks;
using Planner.Backend.Soap.Visualizations;
using Planner.Backend.Soap.Volumes;

internal static class CatalogTests
{
    public static async Task Run()
    {
        var gateway = new FakeDtuGateway();
        var options = Options.Create(new DtuOptions { CourseBatchSize = 25 });
        using var cache = new DtuCache(options);
        var service = new CourseCatalogService(gateway, cache, options, NullLogger<CourseCatalogService>.Instance);
        var codes = gateway.CourseXml.SelectNodes("//Course")!.OfType<XmlElement>().Select(c => c.GetAttribute("CourseCode")).ToArray();
        var result = await service.GetCoursesForStudyPlanAsync(2026, codes.Concat(["99999"]));
        Require(result.Courses.Count == 21 && gateway.CourseCalls == 1, "21 courses must load in one batch");
        Require(result.MissingCourseCodes.SequenceEqual(["99999"]), "Missing codes must be distinct from loaded courses");
        Require(gateway.LastYear == "2026/2027", "Course service must send the academic-year identifier");
        var math = result.Courses.Single(c => c.CourseCode == "01001");
        Require(math.Ects == 10 && math.CourseLevel == "bsc" && math.GradingMode == "graded" && math.ExaminerMode == "external", "Course metadata changed");
        Require(math.PlacementOptions.Count == 2 && math.TimeBlocks.SequenceEqual(new[] { "E1A", "E2A", "E2B" }), "Alternative schedules must retain normalized blocks");
        var repeatedPhysics = (XmlElement)gateway.CourseXml.SelectSingleNode("//Course[@CourseCode='10060']")!.CloneNode(true);
        ((XmlElement)repeatedPhysics.SelectSingleNode(".//Schedule_Txt[@Lang='en-GB']")!).SetAttribute("Txt",
            "Scheme A: F1A and E1A. Scheme B: F5B and E5B. Scheme C: F1B and E1B. " +
            "From Spring 2025: Scheme A: F1A and E1A. Scheme B: F5B and E5B. Scheme C: F1B and E1B. Scheme D: F3A and E3A.");
        var historicalPhysics = CourseXmlParser.Parse(repeatedPhysics);
        Require(historicalPhysics.PlacementOptions.Select(option => option.Id).SequenceEqual(["A", "B", "C", "D"]),
            "Repeated historical schedule sections must produce unique scheme IDs");
        Require(historicalPhysics.PlacementOptions.Single(option => option.Id == "B").TimeBlocks.SequenceEqual(["E5B", "F5B"]),
            "Deduplicating schemes must retain their teaching blocks");
        Require(result.Courses.Any(c => c.TimeBlocks.Any(b => b is "JANUARY" or "JUNE" or "JULY" or "AUGUST")), "Fixture should cover intensive courses");
        await Task.WhenAll(service.GetCoursesForStudyPlanAsync(2026, codes), service.GetCoursesForStudyPlanAsync(2026, codes));
        Require(gateway.CourseCalls == 1, "Repeated imports must be cache hits");

        using var concurrentCache = new DtuCache(options);
        var concurrent = new CourseCatalogService(gateway, concurrentCache, options, NullLogger<CourseCatalogService>.Instance);
        await Task.WhenAll(concurrent.GetCoursesForStudyPlanAsync(2026, codes), concurrent.GetCoursesForStudyPlanAsync(2026, codes));
        Require(gateway.CourseCalls == 2, "Concurrent identical cache fills must share a batch");

        var historical = await service.GetCoursesForStudyPlanAsync(2025, ["01001"]);
        Require(historical.Courses.Count == 0 && historical.MissingCourseCodes.Contains("01001"), "An unavailable historical catalogue must preserve missing codes");
        var wrongYear = await service.GetCoursesForStudyPlanAsync(2024, ["01001"]);
        Require(wrongYear.Courses.Count == 0, "Never accept a response from the wrong academic year");
        Require(gateway.SingleCourseCalls == 0, "Historical lookup must be opt-in");
        var recovered = await service.GetCoursesForStudyPlanAsync(2025, ["01001", "99999"], true);
        Require(recovered.Courses.Single().SourceVolume == "2025/2026" &&
            recovered.MissingCourseCodes.SequenceEqual(["99999"]), "Fallback must recover the exact year and preserve missing codes");
        Require(gateway.SingleCourseCalls == 2 && gateway.LastYear == "2025/2026", "Fallback must request each code in the specified year");
        await service.GetCoursesForStudyPlanAsync(2025, ["01001", "99999"], true);
        Require(gateway.SingleCourseCalls == 2, "Historical results must be cached");
        await service.GetCoursesForStudyPlanAsync(2026, ["01001", "99999"], true);
        Require(gateway.SingleCourseCalls == 2, "A nonempty batch must not trigger fallback for missing codes");
        var mismatched = await service.GetCoursesForStudyPlanAsync(2024, ["01001"], true);
        Require(mismatched.Courses.Count == 0, "Fallback must reject a course from another year");
        var disabledAgain = await service.GetCoursesForStudyPlanAsync(2025, ["01001"]);
        Require(disabledAgain.Courses.Count == 0, "Historical cache must not change normal lookup behavior");
        var incompleteXml = new XmlDocument();
        incompleteXml.LoadXml("<Course CourseCode='12345' Volume='2026/2027'><Title Lang='en-GB' Title='Incomplete'/></Course>");
        var incomplete = CourseXmlParser.Parse(incompleteXml.DocumentElement!);
        Require(incomplete.Ects is null && incomplete.ExaminerMode == "unknown" && incomplete.DataWarnings.Count > 0,
            "Missing metadata must remain unknown and visible, not be invented");

        gateway.FailCourses = true;
        try { await service.GetCoursesForStudyPlanAsync(2026, ["12345"]); throw new Exception("Expected failure"); }
        catch (DtuUnavailableException) { }
        gateway.FailCourses = false;
        var calls = gateway.CourseCalls;
        await service.GetCoursesForStudyPlanAsync(2026, ["12345"]);
        Require(gateway.CourseCalls == calls + 1, "Transport failures must not be negative-cached");
        calls = gateway.CourseCalls;
        try { await service.GetCoursesForStudyPlanAsync(2026, ["invalid"]); throw new Exception("Expected input rejection"); }
        catch (ArgumentException) { }
        Require(gateway.CourseCalls == calls, "Invalid codes must not call DTU");

        using var chunkCache = new DtuCache(options);
        var chunked = new CourseCatalogService(gateway, chunkCache, Options.Create(new DtuOptions { CourseBatchSize = 10 }), NullLogger<CourseCatalogService>.Instance);
        await chunked.GetCoursesForStudyPlanAsync(2026, codes);
        Require(gateway.CourseCalls == calls + 3, "21 courses at batch size 10 must use three requests");

        var search = await service.SearchCoursesAsync(2026, " 01001 ");
        Require(search.Courses.Single().CourseCode == "01001" && gateway.LastSearchCode == "01001" && gateway.LastSearchWords == "",
            "Five digit queries must use exact courseCode after trimming");
        search = await service.SearchCoursesAsync(2026, " learning ");
        Require(gateway.LastSearchCode == "" && gateway.LastSearchWords == "learning" && gateway.LastYear == "2026/2027",
            "Text search must use searchWords and the selected academic year");
        Require(search.Courses.Count == 21 && search.Courses.Select(c => c.CourseCode).SequenceEqual(codes.Order(StringComparer.Ordinal)),
            "Search must parse all results and sort by code");
        Require(search.Courses.Single(c => c.CourseCode == "01001").PlacementOptions.Count == 2,
            "Search cards must retain complete course metadata");
        await service.SearchCoursesAsync(2026, "123");
        Require(gateway.LastSearchWords == "123", "Non-five-digit queries must use text search");
        search = await service.SearchCoursesAsync(2024, "learning");
        Require(search.Courses.Count == 0 && search.MissingCourseCodes.Count == 0, "Search must reject other catalogue years");
        gateway.CourseXml.DocumentElement!.AppendChild(gateway.CourseXml.SelectSingleNode("//Course")!.CloneNode(true));
        search = await service.SearchCoursesAsync(2026, "learning");
        Require(search.Courses.Count == 21, "Search must deduplicate courses");
        calls = gateway.CourseCalls;
        foreach (var query in new string?[] { null, "", "  " })
        {
            try { await service.SearchCoursesAsync(2026, query); throw new Exception("Expected empty search rejection"); }
            catch (ArgumentException) { }
        }
        Require(gateway.CourseCalls == calls, "Empty searches must not reach DTU");
        gateway.FailCourses = true;
        try { await service.SearchCoursesAsync(2026, "learning"); throw new Exception("Expected search failure"); }
        catch (DtuUnavailableException) { }
        gateway.FailCourses = false;
        Require((await service.SearchCoursesAsync(2026, "learning")).Courses.Count == 21, "Failed searches must be retryable");
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

internal sealed class FakeDtuGateway : IDtuGateway
{
    public string? LastSearchCode { get; private set; }
    public string? LastSearchWords { get; private set; }
    public Task<XmlElement> SearchCoursesAsync(AcademicYear year, string courseCode, string searchWords)
    {
        LastSearchCode = courseCode;
        LastSearchWords = searchWords;
        return GetCoursesAsync(year, []);
    }
    public XmlDocument CourseXml { get; } = Load("courses-2026-2027.xml");
    public int CourseCalls { get; private set; }
    public int SingleCourseCalls { get; private set; }
    public int HtmlCalls { get; private set; }
    public string? LastYear { get; private set; }
    public bool FailCourses { get; set; }
    public Education[] Educations { get; set; } = [];
    public StudyBoxInfo[] StudyBoxes { get; set; } = [];
    public Visualization[] Views { get; set; } = [];
    public string Html { get; set; } = "";
    public Volume[] Volumes { get; set; } = [];

    public async Task<XmlElement> GetCoursesAsync(AcademicYear year, IReadOnlyCollection<string> codes)
    {
        CourseCalls++;
        LastYear = year.Catalogue;
        await Task.Delay(10);
        if (FailCourses) throw new DtuUnavailableException("test", new TimeoutException());
        if (year.StartYear == 2025) return Load("courses-2025-2026.xml").DocumentElement!;
        return CourseXml.DocumentElement!; // Includes unrequested codes: the service must filter them.
    }
    public Task<Education[]> GetEducationsAsync(int volume) => Task.FromResult(Educations);
    public Task<XmlNode> GetCourseAsync(AcademicYear year, string code)
    {
        SingleCourseCalls++;
        LastYear = year.Catalogue;
        var xml = new XmlDocument();
        xml.LoadXml("<root/>");
        var source = CourseXml.SelectNodes("//Course")!.OfType<XmlElement>()
            .FirstOrDefault(course => course.GetAttribute("CourseCode") == code);
        if (source is not null)
        {
            var course = (XmlElement)xml.ImportNode(source, true);
            course.SetAttribute("Volume", "2025/2026");
            xml.DocumentElement!.AppendChild(course);
        }
        return Task.FromResult<XmlNode>(xml.DocumentElement!);
    }
    public Task<Volume[]> GetVolumesAsync() => Task.FromResult(Volumes);
    public Task<XmlElement> GetCatalogueVersionsAsync()
    {
        var xml = new XmlDocument();
        xml.LoadXml("<root><VolumeSetting Volume='2026/2027' Preferred='1'/></root>");
        return Task.FromResult(xml.DocumentElement!);
    }
    public Task<StudyBoxInfo[]> GetStudyBoxesAsync(int volume) => Task.FromResult(StudyBoxes);
    public Task<Visualization[]> GetVisualizationsAsync(int educationId) => Task.FromResult(Views);
    public Task<string> GetVisualizationAsync(int id, string code, int volume, string language)
    {
        HtmlCalls++;
        return Task.FromResult(Html);
    }
    private static XmlDocument Load(string file)
    {
        var xml = new XmlDocument();
        xml.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", file));
        return xml;
    }
}
