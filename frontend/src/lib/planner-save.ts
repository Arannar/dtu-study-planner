import {
	isSyntheticActivity,
	type CourseSummary,
	type SavedStudyPlan,
	type StudyPlan
} from './planner';

export type BuildSavedStudyPlanInput = {
	availableCourses: CourseSummary[];
	plan: StudyPlan;
	volume: string;
	semesterCount: number;
	selectedPlacementByCourseCode: Record<string, string>;
};

export function buildSavedStudyPlan({
	availableCourses,
	plan,
	volume,
	semesterCount,
	selectedPlacementByCourseCode
}: BuildSavedStudyPlanInput): SavedStudyPlan {
	const importedCourseCodes = availableCourses
		.filter((course) => !isSyntheticActivity(course))
		.map((course) => course.courseCode);
	const importedActivities = availableCourses
		.filter((course) => isSyntheticActivity(course))
		.map((course) => ({
			...course,
			timeBlocks: [...course.timeBlocks],
			placementOptions: [...(course.placementOptions ?? [])]
		}));
	const placementSelections = Object.fromEntries(
		Object.entries(selectedPlacementByCourseCode).filter(([, value]) => Boolean(value))
	);

	return {
		version: 1,
		savedAt: new Date().toISOString(),
		volume,
		semesterCount,
		importedCourseCodes,
		importedActivities,
		selectedPlacementByCourseCode: placementSelections,
		plan: {
			courses: plan.courses.map((course) => ({
				...course,
				timeBlocks: [...course.timeBlocks]
			}))
		}
	};
}

export function isSavedStudyPlan(value: unknown): value is SavedStudyPlan {
	if (!value || typeof value !== 'object') {
		return false;
	}

	const candidate = value as Partial<SavedStudyPlan>;
	return (
		candidate.version === 1 &&
		typeof candidate.volume === 'string' &&
		Array.isArray(candidate.importedCourseCodes) &&
		typeof candidate.selectedPlacementByCourseCode === 'object' &&
		candidate.selectedPlacementByCourseCode !== null &&
		typeof candidate.plan === 'object' &&
		candidate.plan !== null &&
		Array.isArray(candidate.plan.courses)
	);
}
