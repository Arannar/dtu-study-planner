export type GradingMode = 'graded' | 'passFail' | 'unknown';
export type ExaminerMode = 'external' | 'internal' | 'unknown';

export type CourseSummary = {
	courseCode: string;
	title: string;
	courseLevel?: string;
	ects?: number;
	scheduleText?: string;
	gradingMode?: GradingMode;
	examinerMode?: ExaminerMode;
	rawScheduleKeys?: string[];
	timeBlocks: string[];
	selectedPlacementOptionId?: string;
	placementOptions?: CoursePlacementOption[];
	language?: string;
	kind?: 'course' | 'activity';
	activityType?: 'bscProject' | 'bengProject' | 'bengInternship' | 'mscThesis' | 'specialCourse';
	displayCode?: string | null;
	scheduleMode?: 'fixed' | 'freeFill' | 'external';
	bucket?: string | null;
};

export type CoursePlacementOption = {
	id: string;
	label: string;
	timeBlocks: string[];
};

export type PlannedCourse = {
	courseCode: string;
	title: string;
	courseLevel?: string;
	ects?: number;
	semester: number;
	placementOptionId?: string;
	placementOptionLabel?: string;
	gradingMode?: GradingMode;
	examinerMode?: ExaminerMode;
	timeBlocks: string[];
	kind?: 'course' | 'activity';
	activityType?: 'bscProject' | 'bengProject' | 'bengInternship' | 'mscThesis' | 'specialCourse';
	displayCode?: string | null;
	scheduleMode?: 'fixed' | 'freeFill' | 'external';
	bucket?: string | null;
};

export type StudyPlan = {
	courses: PlannedCourse[];
};

export type PlacementResult = {
	allowed: boolean;
	message: string;
	conflictingCourseCodes: string[];
	sharedTimeBlocks: string[];
};

export type SemesterValidationResult = {
	semester: number;
	allowed: boolean;
	totalEcts: number;
	conflicts: PlacementResult[];
};

export type PlanValidationResult = {
	allowed: boolean;
	semesters: SemesterValidationResult[];
	planConflicts: PlacementResult[];
};

export type CoursesResponse = {
	courses: CourseSummary[];
	missingCourseCodes: string[];
};

export type SavedStudyPlan = {
	version: 1;
	savedAt: string;
	volume: string;
	semesterCount?: number;
	importedCourseCodes: string[];
	importedActivities?: CourseSummary[];
	selectedPlacementByCourseCode: Record<string, string>;
	plan: StudyPlan;
};

export type ProgrammeListItem = {
	educationId: number;
	educationStaticGuid: string;
	code: string;
	level: string;
	educationNameDanish?: string;
	educationNameEnglish?: string;
	programmeNameDanish?: string;
	programmeNameEnglish?: string;
	popularTitleDanish?: string;
	popularTitleEnglish?: string;
	isInDanish: boolean;
	isInEnglish: boolean;
};

export type ProgrammeListResponse = {
	volume: number;
	resolvedVolume: number;
	programmes: ProgrammeListItem[];
};

export type ProgrammeBucketLimits = {
	totalEcts: number;
	polytechnicalFoundationEcts: number;
	programmeSpecificEcts: number;
	projectsEcts: number;
	electivesEcts: number;
	mandatoryEcts?: number;
	internshipEcts?: number;
};

export type ProgrammeVisualizationReference = {
	name: string;
	id: number;
	guid: string;
};

export type ProgrammeMandatoryCourse = {
	bucket: string;
	bucketLabel: string;
	sourceOrder: number;
	course: CourseSummary;
};

export type ProgrammeStudyFlowOption = {
	id: string;
	label: string;
	description: string;
	kind: string;
	visualizationId?: number;
	visualizationGuid?: string;
	savedPlan: SavedStudyPlan;
};

export type ProgrammeDefinitionResponse = {
	volume: number;
	resolvedVolume: number;
	language: string;
	programme: ProgrammeListItem;
	bucketLimits?: ProgrammeBucketLimits;
	mandatoryCourses: ProgrammeMandatoryCourse[];
	visualizations: ProgrammeVisualizationReference[];
	recommendedStudyPackageViews: ProgrammeVisualizationReference[];
	studyFlowOptions: ProgrammeStudyFlowOption[];
	approvedMscElectiveCourseCodes: string[];
	missingCourseCodes: string[];
	notes: string[];
};

export const API_BASE_URL =
	import.meta.env.VITE_API_BASE_URL ?? (import.meta.env.DEV ? 'http://localhost:5140' : '');
export const DTU_COURSE_BASE_URL = 'https://kurser.dtu.dk/course';

export const DEFAULT_CODES =
	'01001,02002,34600,02138,30010,01002,10060,34601,02139,34602,02402,30400,01035,22050,34721,34720,02451,26020,22051,42620,27020';

export const MONTH_BLOCKS = ['JANUARY', 'JUNE', 'JULY', 'AUGUST'];
export const MULTI_SEMESTER_START_EVEN_COURSES = new Set(['10060']);
export const TIMETABLE_DAYS = ['M', 'T', 'W', 'T', 'F'] as const;
export const TIMETABLE_TIMES = ['8-12', '13-17', '18-22'] as const;

export type WeeklyTimetableCell = {
	day: (typeof TIMETABLE_DAYS)[number];
	time: (typeof TIMETABLE_TIMES)[number];
	block: string | null;
};

export const WEEKLY_TIMETABLE_LAYOUT: WeeklyTimetableCell[] = [
	{ day: 'M', time: '8-12', block: '1A' },
	{ day: 'T', time: '8-12', block: '3A' },
	{ day: 'W', time: '8-12', block: '5A' },
	{ day: 'T', time: '8-12', block: '2B' },
	{ day: 'F', time: '8-12', block: '4B' },
	{ day: 'M', time: '13-17', block: '2A' },
	{ day: 'T', time: '13-17', block: '4A' },
	{ day: 'W', time: '13-17', block: '5B' },
	{ day: 'T', time: '13-17', block: '1B' },
	{ day: 'F', time: '13-17', block: '3B' },
	{ day: 'M', time: '18-22', block: null },
	{ day: 'T', time: '18-22', block: '7' },
	{ day: 'W', time: '18-22', block: null },
	{ day: 'T', time: '18-22', block: null },
	{ day: 'F', time: '18-22', block: null }
];

const timeBlockOrder = new Map(
	[
		'E1A',
		'E1B',
		'E2A',
		'E2B',
		'E3A',
		'E3B',
		'E4A',
		'E4B',
		'E5A',
		'E5B',
		'E7',
		'F1A',
		'F1B',
		'F2A',
		'F2B',
		'F3A',
		'F3B',
		'F4A',
		'F4B',
		'F5A',
		'F5B',
		'F7',
		...MONTH_BLOCKS
	].map((block, index) => [block, index])
);

export function parseCourseCodes(input: string): string[] {
	return input
		.split(',')
		.map((code) => code.trim())
		.filter(Boolean);
}

export function sortTimeBlocks(blocks: string[]): string[] {
	return [...blocks].sort((left, right) => {
		const leftOrder = timeBlockOrder.get(left) ?? Number.MAX_SAFE_INTEGER;
		const rightOrder = timeBlockOrder.get(right) ?? Number.MAX_SAFE_INTEGER;

		return leftOrder - rightOrder || left.localeCompare(right);
	});
}

export function courseMatchesQuery(course: CourseSummary, query: string): boolean {
	if (!query.trim()) {
		return true;
	}

	const normalized = query.trim().toLowerCase();
	return (
		course.courseCode.toLowerCase().includes(normalized) ||
		(course.displayCode?.toLowerCase().includes(normalized) ?? false) ||
		course.title.toLowerCase().includes(normalized)
	);
}

export function describeConflictCount(count: number): string {
	return count === 1 ? '1 overlap' : `${count} overlaps`;
}

export function courseRequiresOddSemester(timeBlocks: string[]): boolean {
	return timeBlocks.some((block) => block.startsWith('E') || block.toUpperCase() === 'JANUARY');
}

export function courseRequiresEvenSemester(timeBlocks: string[]): boolean {
	return timeBlocks.some((block) => {
		const normalized = block.toUpperCase();
		return (
			normalized.startsWith('F') ||
			normalized === 'JUNE' ||
			normalized === 'JULY' ||
			normalized === 'AUGUST'
		);
	});
}

export function isSemesterCompatible(timeBlocks: string[], semester: number): boolean {
	const hasOddBlocks = courseRequiresOddSemester(timeBlocks);
	const hasEvenBlocks = courseRequiresEvenSemester(timeBlocks);
	const isOddSemester = semester % 2 === 1;

	if (hasOddBlocks && hasEvenBlocks) {
		return true;
	}

	if (hasOddBlocks && !isOddSemester) {
		return false;
	}

	if (hasEvenBlocks && isOddSemester) {
		return false;
	}

	return true;
}

export function getSemesterSpecificTimeBlocks(timeBlocks: string[], semester: number): string[] {
	const hasOddBlocks = courseRequiresOddSemester(timeBlocks);
	const hasEvenBlocks = courseRequiresEvenSemester(timeBlocks);

	if (!(hasOddBlocks && hasEvenBlocks)) {
		return timeBlocks;
	}

	const isOddSemester = semester % 2 === 1;

	return timeBlocks.filter((block) => {
		const normalized = block.toUpperCase();

		if (isOddSemester) {
			return normalized.startsWith('E') || normalized === 'JANUARY';
		}

		return (
			normalized.startsWith('F') ||
			normalized === 'JUNE' ||
			normalized === 'JULY' ||
			normalized === 'AUGUST'
		);
	});
}

export function getOddSemesterBlocks(timeBlocks: string[]): string[] {
	return timeBlocks.filter((block) => {
		const normalized = block.toUpperCase();
		return normalized.startsWith('E') || normalized === 'JANUARY';
	});
}

export function getEvenSemesterBlocks(timeBlocks: string[]): string[] {
	return timeBlocks.filter((block) => {
		const normalized = block.toUpperCase();
		return (
			normalized.startsWith('F') ||
			normalized === 'JUNE' ||
			normalized === 'JULY' ||
			normalized === 'AUGUST'
		);
	});
}

export function isMultiSemesterSpringStartCourse(courseCode: string): boolean {
	return MULTI_SEMESTER_START_EVEN_COURSES.has(courseCode);
}

export function getDisplayBlock(block: string): string {
	const normalized = block.toUpperCase();
	if (normalized.startsWith('E') || normalized.startsWith('F')) {
		return normalized.slice(1);
	}

	return normalized;
}

export function getSemesterIntensiveBlocks(semester: number): string[] {
	return semester % 2 === 1 ? ['JANUARY'] : ['JUNE', 'JULY', 'AUGUST'];
}

export function getSelectedPlacementOption(course: CourseSummary, selectedOptionId?: string) {
	const placementOptions = course.placementOptions ?? [];
	if (placementOptions.length === 0) {
		return null;
	}

	return (
		placementOptions.find((option) => option.id === selectedOptionId) ??
		placementOptions.find((option) => option.id === course.selectedPlacementOptionId) ??
		placementOptions[0]
	);
}

export function getCourseDatabaseUrl(courseCode: string): string {
	return `${DTU_COURSE_BASE_URL}/${encodeURIComponent(courseCode)}`;
}

export function isSyntheticActivity(
	course: Pick<CourseSummary, 'kind'> | Pick<PlannedCourse, 'kind'>
): boolean {
	return course.kind === 'activity';
}
