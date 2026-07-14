namespace Planner.Backend.Models;

public sealed class ProgrammeListResponse
{
    public int Volume { get; init; }
    public int ResolvedVolume { get; init; }
    public List<ProgrammeListItem> Programmes { get; init; } = [];
}

public sealed class ProgrammeListItem
{
    public int EducationId { get; init; }
    public Guid EducationStaticGuid { get; init; }
    public string Code { get; init; } = "";
    public string Level { get; init; } = "";
    public string? EducationNameDanish { get; init; }
    public string? EducationNameEnglish { get; init; }
    public string? ProgrammeNameDanish { get; init; }
    public string? ProgrammeNameEnglish { get; init; }
    public string? PopularTitleDanish { get; init; }
    public string? PopularTitleEnglish { get; init; }
    public bool IsInDanish { get; init; }
    public bool IsInEnglish { get; init; }
}

public sealed class ProgrammeDefinitionResponse
{
    public int Volume { get; init; }
    public int ResolvedVolume { get; init; }
    public string Language { get; init; } = "";
    public required ProgrammeListItem Programme { get; init; }
    public ProgrammeBucketLimits? BucketLimits { get; init; }
    public List<ProgrammeMandatoryCourse> MandatoryCourses { get; init; } = [];
    public List<string> ApprovedMscElectiveCourseCodes { get; init; } = [];
    public List<ProgrammeVisualizationReference> Visualizations { get; init; } = [];
    public List<ProgrammeVisualizationReference> RecommendedStudyPackageViews { get; init; } = [];
    public List<ProgrammeStudyFlowOption> StudyFlowOptions { get; init; } = [];
    public List<string> MissingCourseCodes { get; init; } = [];
    public List<string> Notes { get; init; } = [];
}

public sealed class ProgrammeBucketLimits
{
    public double TotalEcts { get; init; }
    public double PolytechnicalFoundationEcts { get; init; }
    public double ProgrammeSpecificEcts { get; init; }
    public double ProjectsEcts { get; init; }
    public double ElectivesEcts { get; init; }
    public double? MandatoryEcts { get; init; }
    public double? InternshipEcts { get; init; }
}

public sealed class ProgrammeMandatoryCourse
{
    public string Bucket { get; init; } = "";
    public string BucketLabel { get; init; } = "";
    public int SourceOrder { get; init; }
    public required CourseSummary Course { get; init; }
}

public sealed class ProgrammeVisualizationReference
{
    public string Name { get; init; } = "";
    public int Id { get; init; }
    public Guid Guid { get; init; }
}

public sealed class ProgrammeStudyFlowOption
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public string Description { get; init; } = "";
    public string Kind { get; init; } = "";
    public int? VisualizationId { get; init; }
    public Guid? VisualizationGuid { get; init; }
    public required SavedStudyPlanDto SavedPlan { get; init; }
}

public sealed class SavedStudyPlanDto
{
    public int Version { get; init; } = 1;
    public string SavedAt { get; init; } = "";
    public string Volume { get; init; } = "";
    public Dictionary<string, string> SelectedPlacementByCourseCode { get; init; } = [];
    public List<string> ImportedCourseCodes { get; init; } = [];
    public required StudyPlan Plan { get; init; }
}
