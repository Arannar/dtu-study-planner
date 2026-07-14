using Planner.Backend.Models;
using Planner.Backend.Services;

var tests = new (string Name, Action Test)[]
{
    ("rejects fall course in even semester", RejectsFallCourseInEvenSemester),
    ("detects semester teaching block overlap", DetectsSemesterTeachingBlockOverlap),
    ("allows approved MSc elective in BSc plan", AllowsApprovedMscElectiveInBscPlan),
    ("detects programme elective overflow", DetectsProgrammeElectiveOverflow)
};

var failed = 0;
foreach (var (name, test) in tests)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
    }
}

return failed;

static void RejectsFallCourseInEvenSemester()
{
    var validator = new StudyPlanValidator();
    var result = validator.ValidatePlacement(
        new StudyPlan(),
        Course("01001", semester: 2, level: "bsc", blocks: ["E1A"]));

    AssertFalse(result.Allowed);
    AssertContains("fall", result.Message);
}

static void DetectsSemesterTeachingBlockOverlap()
{
    var validator = new StudyPlanValidator();
    var result = validator.ValidateSemester(
        new StudyPlan
        {
            Courses =
            [
                Course("01001", semester: 1, blocks: ["E1A"]),
                Course("02002", semester: 1, blocks: ["E1A"])
            ]
        },
        1);

    AssertFalse(result.Allowed);
    AssertEqual(1, result.Conflicts.Count);
    AssertSequenceContains("E1A", result.Conflicts[0].SharedTimeBlocks);
}

static void AllowsApprovedMscElectiveInBscPlan()
{
    var validator = new StudyPlanValidator();
    var result = validator.ValidatePlacement(
        new StudyPlan(),
        Course("42000", semester: 1, level: "msc", blocks: ["E2A"]),
        programmeLevel: "bsc",
        approvedMscElectiveCourseCodes: ["42000"]);

    AssertTrue(result.Allowed);
}

static void DetectsProgrammeElectiveOverflow()
{
    var validator = new StudyPlanValidator();
    var result = validator.ValidatePlan(
        new StudyPlan
        {
            Courses = [Course("01001", ects: 50, bucket: "electives")]
        },
        programmeLevel: "bsc",
        bucketLimits: new ProgrammeBucketLimits
        {
            TotalEcts = 180,
            PolytechnicalFoundationEcts = 55,
            ProgrammeSpecificEcts = 55,
            ProjectsEcts = 25,
            ElectivesEcts = 45
        });

    AssertFalse(result.Allowed);
    AssertTrue(result.PlanConflicts.Count > 0);
}

static PlannedCourse Course(
    string code,
    int semester = 1,
    string? level = "bsc",
    double? ects = 5,
    string? bucket = null,
    List<string>? blocks = null)
{
    return new PlannedCourse
    {
        CourseCode = code,
        Title = code,
        CourseLevel = level,
        Ects = ects,
        Semester = semester,
        Bucket = bucket,
        TimeBlocks = blocks ?? []
    };
}

static void AssertTrue(bool condition)
{
    if (!condition)
    {
        throw new InvalidOperationException("Expected condition to be true.");
    }
}

static void AssertFalse(bool condition)
{
    if (condition)
    {
        throw new InvalidOperationException("Expected condition to be false.");
    }
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}

static void AssertContains(string expected, string actual)
{
    if (!actual.Contains(expected, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"Expected '{actual}' to contain '{expected}'.");
    }
}

static void AssertSequenceContains<T>(T expected, IEnumerable<T> actual)
{
    if (!actual.Contains(expected))
    {
        throw new InvalidOperationException($"Expected sequence to contain {expected}.");
    }
}
