using Planner.Backend.Models;

namespace Planner.Backend.Services;

public interface IStudyPlanValidator
{
    PlacementResult ValidatePlacement(
        StudyPlan plan,
        PlannedCourse candidate,
        string? programmeLevel = null,
        IReadOnlyCollection<string>? approvedMscElectiveCourseCodes = null,
        IReadOnlyCollection<string>? mandatoryCourseCodes = null,
        ProgrammeBucketLimits? bucketLimits = null);
    SemesterValidationResult ValidateSemester(StudyPlan plan, int semester);
    PlanValidationResult ValidatePlan(
        StudyPlan plan,
        string? programmeLevel = null,
        IReadOnlyCollection<string>? mandatoryCourseCodes = null,
        ProgrammeBucketLimits? bucketLimits = null);
}
