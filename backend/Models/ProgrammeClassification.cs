namespace Planner.Backend.Models;

public sealed class ParsedProgrammeCourse
{
    public string CourseCode { get; init; } = "";
    public string Title { get; init; } = "";
    public double? Ects { get; init; }
    public string Bucket { get; init; } = "";
    public string BucketLabel { get; init; } = "";
    public int SourceOrder { get; init; }
}

public sealed record ProgrammeStudyBoxClassification
{
    public List<ParsedProgrammeCourse> MandatoryCourses { get; init; } = [];
    public List<string> ApprovedMscElectiveCourseCodes { get; init; } = [];
    public int? SourceVolume { get; init; }
    public bool HasData => MandatoryCourses.Count > 0 || ApprovedMscElectiveCourseCodes.Count > 0;
}

