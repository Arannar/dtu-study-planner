using Planner.Backend.Models;

namespace Planner.Backend.Services;

public sealed class StudyPlanValidator : IStudyPlanValidator
{
    public PlacementResult ValidatePlacement(
        StudyPlan plan,
        PlannedCourse candidate,
        string? programmeLevel = null,
        IReadOnlyCollection<string>? approvedMscElectiveCourseCodes = null,
        IReadOnlyCollection<string>? mandatoryCourseCodes = null,
        ProgrammeBucketLimits? bucketLimits = null)
    {
        var bscMscElectiveError = ValidateBscMscElectivePlacement(
            candidate,
            programmeLevel,
            approvedMscElectiveCourseCodes);
        if (bscMscElectiveError is not null)
        {
            return bscMscElectiveError;
        }

        var semesterParityError = ValidateSemesterParity(candidate);
        if (semesterParityError is not null)
        {
            return semesterParityError;
        }

        var sameSemester = plan.Courses
            .Where(c => c.Semester == candidate.Semester)
            .ToList();

        var conflicts = sameSemester
            .Where(existing => existing.TimeBlocks.Intersect(candidate.TimeBlocks, StringComparer.OrdinalIgnoreCase).Any())
            .Select(existing => existing.CourseCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sharedTimeBlocks = sameSemester
            .SelectMany(existing => existing.TimeBlocks.Intersect(candidate.TimeBlocks, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (conflicts.Count > 0)
        {
            return new PlacementResult
            {
                Allowed = false,
                Message = "Course overlaps with an existing course in the same semester.",
                ConflictingCourseCodes = conflicts,
                SharedTimeBlocks = sharedTimeBlocks
            };
        }

        var requirementError = ValidateProgrammeRequirements(
            new StudyPlan
            {
                Courses = [.. plan.Courses, candidate]
            },
            programmeLevel,
            mandatoryCourseCodes,
            bucketLimits).FirstOrDefault();
        if (requirementError is not null)
        {
            return requirementError;
        }

        return new PlacementResult
        {
            Allowed = true,
            Message = "Placement allowed."
        };
    }

    public SemesterValidationResult ValidateSemester(StudyPlan plan, int semester)
    {
        var semesterCourses = plan.Courses
            .Where(c => c.Semester == semester)
            .ToList();

        var conflicts = semesterCourses
            .Select(ValidateSemesterParity)
            .Where(result => result is not null)
            .Cast<PlacementResult>()
            .ToList();

        for (var i = 0; i < semesterCourses.Count; i++)
        {
            for (var j = i + 1; j < semesterCourses.Count; j++)
            {
                var a = semesterCourses[i];
                var b = semesterCourses[j];

                var shared = a.TimeBlocks
                    .Intersect(b.TimeBlocks, StringComparer.OrdinalIgnoreCase)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (shared.Count == 0)
                    continue;

                conflicts.Add(new PlacementResult
                {
                    Allowed = false,
                    Message = $"{a.CourseCode} overlaps with {b.CourseCode}.",
                    ConflictingCourseCodes = new List<string> { a.CourseCode, b.CourseCode },
                    SharedTimeBlocks = shared
                });
            }
        }

        var totalEcts = semesterCourses.Sum(c => c.Ects ?? 0);

        return new SemesterValidationResult
        {
            Semester = semester,
            Allowed = conflicts.Count == 0,
            TotalEcts = totalEcts,
            Conflicts = conflicts
        };
    }

    public PlanValidationResult ValidatePlan(
        StudyPlan plan,
        string? programmeLevel = null,
        IReadOnlyCollection<string>? mandatoryCourseCodes = null,
        ProgrammeBucketLimits? bucketLimits = null)
    {
        var semesters = plan.Courses
            .Select(c => c.Semester)
            .Distinct()
            .OrderBy(x => x)
            .Select(semester => ValidateSemester(plan, semester))
            .ToList();

        var planConflicts = ValidateProgrammeRequirements(
            plan,
            programmeLevel,
            mandatoryCourseCodes,
            bucketLimits);

        return new PlanValidationResult
        {
            Allowed = semesters.All(s => s.Allowed) && planConflicts.Count == 0,
            Semesters = semesters,
            PlanConflicts = planConflicts
        };
    }

    private static double TryReadEcts(string _)
    {
        return 0;
    }

    private static PlacementResult? ValidateSemesterParity(PlannedCourse course)
    {
        var hasFallBlocks = course.TimeBlocks.Any(block => block.StartsWith("E", StringComparison.OrdinalIgnoreCase));
        var hasSpringBlocks = course.TimeBlocks.Any(block => block.StartsWith("F", StringComparison.OrdinalIgnoreCase));
        var isOddSemester = course.Semester % 2 == 1;

        if (hasFallBlocks && !isOddSemester)
        {
            return new PlacementResult
            {
                Allowed = false,
                Message = $"{course.CourseCode} contains Fall blocks and can only be placed in odd-numbered semesters.",
                ConflictingCourseCodes = new List<string> { course.CourseCode }
            };
        }

        if (hasSpringBlocks && isOddSemester)
        {
            return new PlacementResult
            {
                Allowed = false,
                Message = $"{course.CourseCode} contains Spring blocks and can only be placed in even-numbered semesters.",
                ConflictingCourseCodes = new List<string> { course.CourseCode }
            };
        }

        return null;
    }

    private static PlacementResult? ValidateBscMscElectivePlacement(
        PlannedCourse course,
        string? programmeLevel,
        IReadOnlyCollection<string>? approvedMscElectiveCourseCodes)
    {
        if (!string.Equals(programmeLevel, "bsc", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!string.Equals(course.CourseLevel, "msc", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var approvedCodes = new HashSet<string>(
            approvedMscElectiveCourseCodes ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);

        if (approvedCodes.Contains(course.CourseCode))
        {
            return null;
        }

        return new PlacementResult
        {
            Allowed = false,
            Message = $"{course.CourseCode} is an MSc-level course and is not pre-approved for this BSc programme.",
            ConflictingCourseCodes = new List<string> { course.CourseCode }
        };
    }

    private static List<PlacementResult> ValidateProgrammeRequirements(
        StudyPlan plan,
        string? programmeLevel,
        IReadOnlyCollection<string>? mandatoryCourseCodes,
        ProgrammeBucketLimits? bucketLimits)
    {
        if (bucketLimits is null || string.IsNullOrWhiteSpace(programmeLevel))
        {
            return [];
        }

        var totals = CalculateRequirementTotals(plan, programmeLevel, mandatoryCourseCodes, bucketLimits);
        var conflicts = new List<PlacementResult>();

        if (totals.TotalEcts > totals.TotalLimit + 0.0001)
        {
            conflicts.Add(BuildPlanConflict(
                $"Planned ECTS exceed the programme maximum by {(totals.TotalEcts - totals.TotalLimit):0.##} ECTS.",
                plan.Courses));
        }

        if (totals.PolytechnicalFoundationLimit > 0 &&
            totals.RawPolytechnicalFoundation > totals.PolytechnicalFoundationLimit + 0.0001)
        {
            conflicts.Add(BuildPlanConflict(
                $"Polytechnical foundation exceeds the allowed limit by {(totals.RawPolytechnicalFoundation - totals.PolytechnicalFoundationLimit):0.##} ECTS.",
                GetBucketCourses(totals.CoursesByBucket, "polytechnicalFoundation")));
        }

        if (totals.ProgrammeLevel is "bsc" or "msc")
        {
            if (totals.EffectiveElectives > totals.ElectivesLimit + 0.0001)
            {
                conflicts.Add(BuildPlanConflict(
                    $"Elective capacity is exceeded by {(totals.EffectiveElectives - totals.ElectivesLimit):0.##} ECTS after programme-specific/project spillover.",
                    GetBucketCourses(totals.CoursesByBucket, "electives")
                        .Concat(totals.ProgrammeSpecificOverflow > 0
                            ? GetBucketCourses(totals.CoursesByBucket, "programmeSpecific")
                            : Array.Empty<PlannedCourse>())
                        .Concat(totals.ProjectOverflow > 0
                            ? GetBucketCourses(totals.CoursesByBucket, "projects")
                            : Array.Empty<PlannedCourse>())));
            }
        }
        else if (totals.ProgrammeLevel == "beng")
        {
            if (totals.MandatoryLimit > 0 && totals.RawMandatory > totals.MandatoryLimit + 0.0001)
            {
                conflicts.Add(BuildPlanConflict(
                    $"Mandatory courses exceed the allowed limit by {(totals.RawMandatory - totals.MandatoryLimit):0.##} ECTS.",
                    GetBucketCourses(totals.CoursesByBucket, "mandatory")));
            }

            if (totals.RawElectives > totals.ElectivesLimit + 0.0001)
            {
                conflicts.Add(BuildPlanConflict(
                    $"Electives exceed the allowed limit by {(totals.RawElectives - totals.ElectivesLimit):0.##} ECTS.",
                    GetBucketCourses(totals.CoursesByBucket, "electives")));
            }

            if (totals.InternshipLimit > 0 && totals.RawInternship > totals.InternshipLimit + 0.0001)
            {
                conflicts.Add(BuildPlanConflict(
                    $"Internship exceeds the allowed limit by {(totals.RawInternship - totals.InternshipLimit):0.##} ECTS.",
                    GetBucketCourses(totals.CoursesByBucket, "internship")));
            }

            if (totals.ProjectsLimit > 0 && totals.RawProjects > totals.ProjectsLimit + 0.0001)
            {
                conflicts.Add(BuildPlanConflict(
                    $"Diploma project exceeds the allowed limit by {(totals.RawProjects - totals.ProjectsLimit):0.##} ECTS.",
                    GetBucketCourses(totals.CoursesByBucket, "projects")));
            }
        }

        return conflicts;
    }

    private static RequirementTotals CalculateRequirementTotals(
        StudyPlan plan,
        string? programmeLevel,
        IReadOnlyCollection<string>? mandatoryCourseCodes,
        ProgrammeBucketLimits bucketLimits)
    {
        var normalizedProgrammeLevel = programmeLevel?.Trim().ToLowerInvariant() ?? string.Empty;
        var mandatorySet = new HashSet<string>(
            mandatoryCourseCodes ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
        var coursesByBucket = new Dictionary<string, List<PlannedCourse>>(StringComparer.OrdinalIgnoreCase);

        double totalEcts = 0;
        double rawPoly = 0;
        double rawProgrammeSpecific = 0;
        double rawProjects = 0;
        double rawElectives = 0;
        double rawMandatory = 0;
        double rawInternship = 0;

        foreach (var course in plan.Courses)
        {
            var ects = course.Ects ?? 0;
            totalEcts += ects;

            var bucket = ResolveCourseBucket(course, normalizedProgrammeLevel, mandatorySet);
            if (!coursesByBucket.TryGetValue(bucket, out var bucketCourses))
            {
                bucketCourses = [];
                coursesByBucket[bucket] = bucketCourses;
            }

            bucketCourses.Add(course);

            switch (bucket)
            {
                case "polytechnicalFoundation":
                    rawPoly += ects;
                    break;
                case "programmeSpecific":
                    rawProgrammeSpecific += ects;
                    break;
                case "projects":
                    rawProjects += ects;
                    break;
                case "mandatory":
                    rawMandatory += ects;
                    break;
                case "internship":
                    rawInternship += ects;
                    break;
                default:
                    rawElectives += ects;
                    break;
            }
        }

        var programmeSpecificOverflow = Math.Max(rawProgrammeSpecific - bucketLimits.ProgrammeSpecificEcts, 0);
        var projectOverflow = Math.Max(rawProjects - bucketLimits.ProjectsEcts, 0);

        return new RequirementTotals
        {
            ProgrammeLevel = normalizedProgrammeLevel,
            CoursesByBucket = coursesByBucket,
            TotalEcts = totalEcts,
            TotalLimit = bucketLimits.TotalEcts,
            PolytechnicalFoundationLimit = bucketLimits.PolytechnicalFoundationEcts,
            ProgrammeSpecificLimit = bucketLimits.ProgrammeSpecificEcts,
            ProjectsLimit = bucketLimits.ProjectsEcts,
            ElectivesLimit = bucketLimits.ElectivesEcts,
            MandatoryLimit = bucketLimits.MandatoryEcts ?? 0,
            InternshipLimit = bucketLimits.InternshipEcts ?? 0,
            RawPolytechnicalFoundation = rawPoly,
            RawProgrammeSpecific = rawProgrammeSpecific,
            RawProjects = rawProjects,
            RawElectives = rawElectives,
            RawMandatory = rawMandatory,
            RawInternship = rawInternship,
            ProgrammeSpecificOverflow = programmeSpecificOverflow,
            ProjectOverflow = projectOverflow,
            EffectiveElectives = rawElectives + programmeSpecificOverflow + projectOverflow
        };
    }

    private static string ResolveCourseBucket(
        PlannedCourse course,
        string programmeLevel,
        IReadOnlySet<string> mandatoryCourseCodes)
    {
        if (!string.IsNullOrWhiteSpace(course.Bucket))
        {
            return course.Bucket;
        }

        if (string.Equals(course.ActivityType, "bengInternship", StringComparison.OrdinalIgnoreCase))
        {
            return "internship";
        }

        if (string.Equals(course.ActivityType, "specialCourse", StringComparison.OrdinalIgnoreCase))
        {
            return "electives";
        }

        if (string.Equals(course.ActivityType, "bscProject", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(course.ActivityType, "bengProject", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(course.ActivityType, "mscThesis", StringComparison.OrdinalIgnoreCase))
        {
            return "projects";
        }

        if (string.Equals(programmeLevel, "beng", StringComparison.OrdinalIgnoreCase) &&
            mandatoryCourseCodes.Contains(course.CourseCode))
        {
            return "mandatory";
        }

        return "electives";
    }

    private static PlacementResult BuildPlanConflict(string message, IEnumerable<PlannedCourse> courses)
    {
        return new PlacementResult
        {
            Allowed = false,
            Message = message,
            ConflictingCourseCodes = courses
                .Select(course => course.CourseCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private static IEnumerable<PlannedCourse> GetBucketCourses(
        IReadOnlyDictionary<string, List<PlannedCourse>> coursesByBucket,
        string bucket)
    {
        return coursesByBucket.TryGetValue(bucket, out var courses)
            ? courses
            : Array.Empty<PlannedCourse>();
    }

    private sealed class RequirementTotals
    {
        public string ProgrammeLevel { get; init; } = "";
        public Dictionary<string, List<PlannedCourse>> CoursesByBucket { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public double TotalEcts { get; init; }
        public double TotalLimit { get; init; }
        public double PolytechnicalFoundationLimit { get; init; }
        public double ProgrammeSpecificLimit { get; init; }
        public double ProjectsLimit { get; init; }
        public double ElectivesLimit { get; init; }
        public double MandatoryLimit { get; init; }
        public double InternshipLimit { get; init; }
        public double RawPolytechnicalFoundation { get; init; }
        public double RawProgrammeSpecific { get; init; }
        public double RawProjects { get; init; }
        public double RawElectives { get; init; }
        public double RawMandatory { get; init; }
        public double RawInternship { get; init; }
        public double ProgrammeSpecificOverflow { get; init; }
        public double ProjectOverflow { get; init; }
        public double EffectiveElectives { get; init; }
    }
}
