import {
	isSyntheticActivity,
	sortTimeBlocks,
	type CourseSummary,
	type ExaminerMode,
	type GradingMode,
	type PlacementResult,
	type PlannedCourse,
	type ProgrammeBucketLimits,
	type ProgrammeListItem
} from './planner';

export type ActivityTemplate = {
	id: 'bscProject' | 'bengProject' | 'bengInternship' | 'mscThesis' | 'specialCourse';
	label: string;
	title: string;
	defaultEcts: string;
	ectsOptions: string[];
	bucket: 'projects' | 'electives' | 'internship';
	scheduleMode: 'freeFill' | 'external';
	gradingMode: GradingMode;
	examinerMode: ExaminerMode;
};

export type RequirementBucket =
	| 'polytechnicalFoundation'
	| 'programmeSpecific'
	| 'projects'
	| 'electives'
	| 'mandatory'
	| 'internship';

export type RequirementBarConfig = {
	bucket: RequirementBucket;
	label: string;
	required: number;
	accentClass: string;
};

export type RequirementBar = RequirementBarConfig & {
	planned: number;
	visiblePlanned: number;
	effectiveCapacity: number;
	fillRatio: number;
	capacityRatio: number;
	fulfilled: boolean;
	overflowed: boolean;
};

export type ExamRuleBar = {
	key: 'passFail' | 'external';
	label: string;
	planned: number;
	complementary: number;
	required: number;
	valueLabel: string;
	fillRatio: number;
	fulfilled: boolean;
	overflowed: boolean;
	accentClass: string;
	successSymbol: '✓' | 'X';
};

type CourseIdentity =
	| Pick<CourseSummary, 'activityType' | 'courseCode' | 'kind'>
	| Pick<PlannedCourse, 'activityType' | 'courseCode' | 'kind'>;

type DisplayCourse =
	| Pick<CourseSummary, 'title' | 'displayCode' | 'courseCode' | 'kind'>
	| Pick<PlannedCourse, 'title' | 'displayCode' | 'courseCode' | 'kind'>;

type ExamCourse =
	| Pick<CourseSummary, 'gradingMode' | 'examinerMode' | 'activityType' | 'courseCode' | 'kind'>
	| Pick<PlannedCourse, 'gradingMode' | 'examinerMode' | 'activityType' | 'courseCode' | 'kind'>;

export function normalizeCourseCode(courseCode: string): string {
	return courseCode.trim().toUpperCase();
}

export function getActivityTemplates(programmeLevel: string | null): ActivityTemplate[] {
	const templates: ActivityTemplate[] = [
		{
			id: 'specialCourse',
			label: 'Special course',
			title: 'Special course',
			defaultEcts: '5',
			ectsOptions: ['2.5', '5', '7.5', '10'],
			bucket: 'electives',
			scheduleMode: 'external',
			gradingMode: 'unknown',
			examinerMode: 'internal'
		}
	];

	if (programmeLevel === 'bsc') {
		templates.unshift({
			id: 'bscProject',
			label: 'BSc project',
			title: 'Bachelor project',
			defaultEcts: '15',
			ectsOptions: ['15', '17.5', '20'],
			bucket: 'projects',
			scheduleMode: 'freeFill',
			gradingMode: 'graded',
			examinerMode: 'external'
		});
	} else if (programmeLevel === 'beng') {
		templates.unshift(
			{
				id: 'bengInternship',
				label: 'Internship',
				title: 'Engineering internship',
				defaultEcts: '30',
				ectsOptions: ['30'],
				bucket: 'internship',
				scheduleMode: 'external',
				gradingMode: 'passFail',
				examinerMode: 'internal'
			},
			{
				id: 'bengProject',
				label: 'BEng project',
				title: 'Diploma project',
				defaultEcts: '15',
				ectsOptions: ['15'],
				bucket: 'projects',
				scheduleMode: 'freeFill',
				gradingMode: 'graded',
				examinerMode: 'external'
			}
		);
	} else if (programmeLevel === 'msc') {
		templates.unshift({
			id: 'mscThesis',
			label: 'MSc thesis',
			title: 'Master thesis',
			defaultEcts: '30',
			ectsOptions: ['30', '32.5', '35'],
			bucket: 'projects',
			scheduleMode: 'freeFill',
			gradingMode: 'graded',
			examinerMode: 'external'
		});
	}

	return templates;
}

export function getCourseDisplayCode(course: DisplayCourse): string | null {
	if (course.displayCode !== undefined && course.displayCode !== null) {
		return course.displayCode;
	}

	return isSyntheticActivity(course) ? null : course.courseCode;
}

export function getCourseDisplayName(course: DisplayCourse): string {
	return getCourseDisplayCode(course) ?? course.title;
}

export function getCourseSortKey(course: CourseSummary): string {
	return `${getCourseDisplayCode(course) ?? 'zzzz'} ${course.title}`.toLowerCase();
}

export function getResolvedActivityType(course: CourseIdentity): string | null {
	if (course.activityType) {
		return course.activityType;
	}

	if (!isSyntheticActivity(course)) {
		return null;
	}

	const match = /^activity:(bscProject|bengProject|bengInternship|mscThesis|specialCourse):/i.exec(
		course.courseCode
	);
	return match?.[1] ?? null;
}

export function isProjectActivity(course: CourseIdentity): boolean {
	const activityType = getResolvedActivityType(course);
	return (
		activityType === 'bscProject' || activityType === 'bengProject' || activityType === 'mscThesis'
	);
}

export function getActivityDescriptor(
	course:
		| Pick<CourseSummary, 'scheduleMode' | 'activityType' | 'courseCode' | 'kind'>
		| Pick<PlannedCourse, 'scheduleMode' | 'activityType' | 'courseCode' | 'kind'>
): string {
	const activityType = getResolvedActivityType(course);

	if (activityType === 'bengInternship') {
		return 'Industry placement';
	}

	if (course.scheduleMode === 'external') {
		return 'Outside schedule';
	}

	if (isProjectActivity(course)) {
		return 'Full-time';
	}

	return '';
}

export function createSyntheticActivity(
	template: ActivityTemplate,
	ectsText: string
): CourseSummary {
	const ects = Number.parseFloat(ectsText);
	const identifier = crypto.randomUUID();

	return {
		courseCode: `activity:${template.id}:${ectsText}:${identifier}`,
		title: template.title,
		ects: Number.isFinite(ects) ? ects : undefined,
		timeBlocks: [],
		placementOptions: [],
		kind: 'activity',
		activityType: template.id,
		displayCode: null,
		scheduleMode: template.scheduleMode,
		bucket: template.bucket,
		gradingMode: template.gradingMode,
		examinerMode: template.examinerMode
	};
}

export function getRequirementLimit(
	limits: ProgrammeBucketLimits | undefined,
	bucket: RequirementBucket
): number {
	if (!limits) {
		return 0;
	}

	switch (bucket) {
		case 'polytechnicalFoundation':
			return limits.polytechnicalFoundationEcts;
		case 'programmeSpecific':
			return limits.programmeSpecificEcts;
		case 'projects':
			return limits.projectsEcts;
		case 'electives':
			return limits.electivesEcts;
		case 'mandatory':
			return limits.mandatoryEcts ?? 0;
		case 'internship':
			return limits.internshipEcts ?? 0;
	}
}

export function buildRequirementBarConfigs(
	programmeLevel: string | null,
	bucketLimits: ProgrammeBucketLimits | undefined
): RequirementBarConfig[] {
	if (!programmeLevel || !bucketLimits) {
		return [];
	}

	if (programmeLevel === 'beng') {
		return [
			{
				bucket: 'mandatory',
				label: 'Mandatory',
				required: getRequirementLimit(bucketLimits, 'mandatory'),
				accentClass: 'mandatory'
			},
			{
				bucket: 'electives',
				label: 'Electives',
				required: getRequirementLimit(bucketLimits, 'electives'),
				accentClass: 'electives'
			},
			{
				bucket: 'internship',
				label: 'Internship',
				required: getRequirementLimit(bucketLimits, 'internship'),
				accentClass: 'internship'
			},
			{
				bucket: 'projects',
				label: 'Diploma project',
				required: getRequirementLimit(bucketLimits, 'projects'),
				accentClass: 'projects'
			}
		];
	}

	return [
		{
			bucket: 'polytechnicalFoundation',
			label: 'Polytechnical foundation',
			required: getRequirementLimit(bucketLimits, 'polytechnicalFoundation'),
			accentClass: 'polytechnicalFoundation'
		},
		{
			bucket: 'programmeSpecific',
			label: 'Programme specific',
			required: getRequirementLimit(bucketLimits, 'programmeSpecific'),
			accentClass: 'programmeSpecific'
		},
		{
			bucket: 'electives',
			label: 'Electives',
			required: getRequirementLimit(bucketLimits, 'electives'),
			accentClass: 'electives'
		},
		{
			bucket: 'projects',
			label: programmeLevel === 'msc' ? "Master's thesis" : 'Projects',
			required: getRequirementLimit(bucketLimits, 'projects'),
			accentClass: 'projects'
		}
	];
}

export function resolveCourseBucketForRequirements(
	course: Pick<PlannedCourse, 'bucket' | 'courseCode' | 'activityType' | 'kind'>,
	programmeLevel: string | null,
	mandatoryCodes: Set<string>,
	programmeBucketByCourseCode: Record<string, string>
): string {
	const activityType = getResolvedActivityType(course);

	if (activityType === 'bengInternship') {
		return 'internship';
	}

	if (isSyntheticActivity(course)) {
		if (isProjectActivity(course)) {
			return 'projects';
		}

		if (activityType === 'specialCourse') {
			return 'electives';
		}
	}

	if (programmeLevel === 'beng') {
		return mandatoryCodes.has(normalizeCourseCode(course.courseCode)) ? 'mandatory' : 'electives';
	}

	return programmeBucketByCourseCode[normalizeCourseCode(course.courseCode)] ?? 'electives';
}

export function recategorizePlanCourses(
	courses: PlannedCourse[],
	programmeLevel: string | null,
	mandatoryCodes: Set<string>,
	programmeBucketByCourseCode: Record<string, string>
): PlannedCourse[] {
	return courses.map((course) => ({
		...course,
		bucket: resolveCourseBucketForRequirements(
			course,
			programmeLevel,
			mandatoryCodes,
			programmeBucketByCourseCode
		)
	}));
}

export function isActivityAllowedForProgramme(
	course: CourseIdentity,
	programmeLevel: string | null
): boolean {
	const activityType = getResolvedActivityType(course);

	if (!activityType || activityType === 'specialCourse') {
		return true;
	}

	if (programmeLevel === 'bsc') {
		return activityType === 'bscProject';
	}

	if (programmeLevel === 'beng') {
		return activityType === 'bengProject' || activityType === 'bengInternship';
	}

	if (programmeLevel === 'msc') {
		return activityType === 'mscThesis';
	}

	return false;
}

export function getSyntheticActivityExamMetadata(
	course: CourseIdentity
): { gradingMode: GradingMode; examinerMode: ExaminerMode } | null {
	if (!isSyntheticActivity(course)) {
		return null;
	}

	const activityType = getResolvedActivityType(course);

	if (
		activityType === 'bscProject' ||
		activityType === 'bengProject' ||
		activityType === 'mscThesis'
	) {
		return { gradingMode: 'graded', examinerMode: 'external' };
	}

	if (activityType === 'bengInternship') {
		return { gradingMode: 'passFail', examinerMode: 'internal' };
	}

	if (activityType === 'specialCourse') {
		return { gradingMode: 'unknown', examinerMode: 'internal' };
	}

	return { gradingMode: 'unknown', examinerMode: 'unknown' };
}

export function getCourseGradingMode(course: ExamCourse): GradingMode {
	if (course.gradingMode === 'graded' || course.gradingMode === 'passFail') {
		return course.gradingMode;
	}

	return getSyntheticActivityExamMetadata(course)?.gradingMode ?? 'unknown';
}

export function getCourseExaminerMode(course: ExamCourse): ExaminerMode {
	if (course.examinerMode === 'external' || course.examinerMode === 'internal') {
		return course.examinerMode;
	}

	return getSyntheticActivityExamMetadata(course)?.examinerMode ?? 'unknown';
}

export function buildRequirementBars(
	courses: PlannedCourse[],
	programmeLevel: string | null,
	bucketLimits: ProgrammeBucketLimits | undefined,
	mandatoryCodes: Set<string>,
	programmeBucketByCourseCode: Record<string, string>
): RequirementBar[] {
	const configs = buildRequirementBarConfigs(programmeLevel, bucketLimits);
	if (!bucketLimits || !programmeLevel || configs.length === 0) {
		return [];
	}

	const totals: Record<RequirementBucket, number> = {
		polytechnicalFoundation: 0,
		programmeSpecific: 0,
		projects: 0,
		electives: 0,
		mandatory: 0,
		internship: 0
	};

	for (const course of courses) {
		const bucket = resolveCourseBucketForRequirements(
			course,
			programmeLevel,
			mandatoryCodes,
			programmeBucketByCourseCode
		);
		const ects = course.ects ?? 0;

		if (bucket in totals) {
			totals[bucket as RequirementBucket] += ects;
		} else {
			totals.electives += ects;
		}
	}

	const programmeSpecificOverflow = Math.max(
		totals.programmeSpecific - bucketLimits.programmeSpecificEcts,
		0
	);
	const projectOverflow = Math.max(totals.projects - bucketLimits.projectsEcts, 0);
	const electiveOverflowLoad =
		programmeLevel === 'beng' ? 0 : programmeSpecificOverflow + projectOverflow;
	const effectiveElectiveCapacity = Math.max(bucketLimits.electivesEcts - electiveOverflowLoad, 0);

	return configs.map((config) => {
		const planned = totals[config.bucket];
		const fulfilledPlanned =
			config.bucket === 'electives' ? planned + electiveOverflowLoad : planned;
		const effectiveCapacity =
			config.bucket === 'electives' ? effectiveElectiveCapacity : config.required;
		const denominator = config.required > 0 ? config.required : 1;
		const visiblePlanned = Math.min(planned, effectiveCapacity);

		return {
			...config,
			planned,
			visiblePlanned,
			effectiveCapacity,
			fillRatio: Math.min(visiblePlanned / denominator, 1),
			capacityRatio: Math.min(effectiveCapacity / denominator, 1),
			fulfilled: config.required > 0 && fulfilledPlanned >= config.required,
			overflowed: planned > effectiveCapacity + 0.0001
		};
	});
}

export function buildExamRuleBars(
	courses: PlannedCourse[],
	bucketLimits: ProgrammeBucketLimits | undefined
): ExamRuleBar[] {
	if (!bucketLimits?.totalEcts) {
		return [];
	}

	const threshold = bucketLimits.totalEcts / 3;
	let passFailEcts = 0;
	let gradedEcts = 0;
	let externalEcts = 0;
	let internalEcts = 0;

	for (const course of courses) {
		const ects = course.ects ?? 0;

		switch (getCourseGradingMode(course)) {
			case 'passFail':
				passFailEcts += ects;
				break;
			case 'graded':
				gradedEcts += ects;
				break;
		}

		switch (getCourseExaminerMode(course)) {
			case 'external':
				externalEcts += ects;
				break;
			case 'internal':
				internalEcts += ects;
				break;
		}
	}

	return [
		{
			key: 'passFail',
			label: 'Pass/fail',
			planned: passFailEcts,
			complementary: gradedEcts,
			required: threshold,
			valueLabel: `${passFailEcts.toFixed(1)}/${threshold.toFixed(1)} ECTS (graded ${gradedEcts.toFixed(1)})`,
			fillRatio: Math.min(passFailEcts / threshold, 1),
			fulfilled: false,
			overflowed: passFailEcts > threshold + 0.0001,
			accentClass: 'passFail',
			successSymbol: 'X'
		},
		{
			key: 'external',
			label: 'External examiner',
			planned: externalEcts,
			complementary: internalEcts,
			required: threshold,
			valueLabel: `${externalEcts.toFixed(1)}/${threshold.toFixed(1)} ECTS (internal ${internalEcts.toFixed(1)})`,
			fillRatio: Math.min(externalEcts / threshold, 1),
			fulfilled: externalEcts >= threshold,
			overflowed: false,
			accentClass: 'externalExaminer',
			successSymbol: '✓'
		}
	];
}

export function getProgrammeLabel(programme: ProgrammeListItem): string {
	return (
		programme.popularTitleEnglish ??
		programme.programmeNameEnglish ??
		programme.popularTitleDanish ??
		programme.programmeNameDanish ??
		programme.code
	);
}

export function getCourseBucket(
	course: CourseIdentity,
	programmeBucketByCourseCode: Record<string, string>,
	hasProgrammeDefinition: boolean
): string | null {
	const activityType = getResolvedActivityType(course);

	if (isSyntheticActivity(course)) {
		if (activityType === 'bengInternship') {
			return 'internship';
		}

		if (isProjectActivity(course)) {
			return 'projects';
		}

		if (activityType === 'specialCourse') {
			return 'electives';
		}
	}

	if (!hasProgrammeDefinition) {
		return null;
	}

	return programmeBucketByCourseCode[normalizeCourseCode(course.courseCode)] ?? 'electives';
}

export function isCourseBucket(
	course: CourseIdentity,
	bucket: string,
	programmeBucketByCourseCode: Record<string, string>,
	hasProgrammeDefinition: boolean
): boolean {
	return getCourseBucket(course, programmeBucketByCourseCode, hasProgrammeDefinition) === bucket;
}

export function getTimeRangeParts(time: string): { start: string; end: string } {
	const [start, end] = time.split('-');
	return { start, end };
}

export function formatValidationMessage(result: PlacementResult): string {
	const courseCodes = result.conflictingCourseCodes.join(', ');
	const sharedTimeBlocks = sortTimeBlocks(result.sharedTimeBlocks).join(', ');

	if (sharedTimeBlocks && courseCodes) {
		return `${result.message} ${courseCodes} share ${sharedTimeBlocks}.`;
	}

	if (courseCodes) {
		return `${result.message} ${courseCodes}.`;
	}

	return result.message;
}

export function formatGradingMode(mode: GradingMode): string {
	switch (mode) {
		case 'graded':
			return 'Graded';
		case 'passFail':
			return 'Pass/fail';
		case 'unknown':
			return 'Unknown';
	}
}

export function formatExaminerMode(mode: ExaminerMode): string {
	switch (mode) {
		case 'external':
			return 'External examiner';
		case 'internal':
			return 'Internal examiner';
		case 'unknown':
			return 'Unknown';
	}
}

export function getCourseTooltip(course: CourseSummary): string {
	if (isSyntheticActivity(course)) {
		return [
			getActivityDescriptor(course),
			`Grading: ${formatGradingMode(getCourseGradingMode(course))}`,
			`Examiner: ${formatExaminerMode(getCourseExaminerMode(course))}`
		]
			.filter(Boolean)
			.join('\n');
	}

	return [
		course.scheduleText ? `Schedule info: ${course.scheduleText}` : null,
		`Grading: ${formatGradingMode(getCourseGradingMode(course))}`,
		`Examiner: ${formatExaminerMode(getCourseExaminerMode(course))}`
	]
		.filter(Boolean)
		.join('\n');
}
