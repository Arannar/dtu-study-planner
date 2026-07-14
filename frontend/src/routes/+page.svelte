<script lang="ts">
	import { onMount } from 'svelte';
	import { SvelteMap, SvelteSet } from 'svelte/reactivity';
	import './+page.css';
	import {
		DEFAULT_CODES,
		courseMatchesQuery,
		describeConflictCount,
		getDisplayBlock,
		getCourseDatabaseUrl,
		getEvenSemesterBlocks,
		getOddSemesterBlocks,
		getSemesterSpecificTimeBlocks,
		getSelectedPlacementOption,
		getSemesterIntensiveBlocks,
		isSemesterCompatible,
		isMultiSemesterSpringStartCourse,
		parseCourseCodes,
		sortTimeBlocks,
		TIMETABLE_DAYS,
		TIMETABLE_TIMES,
		WEEKLY_TIMETABLE_LAYOUT,
		isSyntheticActivity,
		type CourseSummary,
		type PlacementResult,
		type PlannedCourse,
		type PlanValidationResult,
		type ProgrammeDefinitionResponse,
		type ProgrammeListItem,
		type SavedStudyPlan,
		type SemesterValidationResult,
		type StudyPlan
	} from '$lib/planner';
	import {
		fetchCourseBatch as fetchCourseBatchFromApi,
		fetchProgrammeDefinition,
		fetchProgrammes,
		validatePlacement,
		validatePlan
	} from '$lib/planner-api';
	import {
		buildSavedStudyPlan as buildSavedStudyPlanPayload,
		isSavedStudyPlan
	} from '$lib/planner-save';
	import {
		buildExamRuleBars,
		buildRequirementBars,
		createSyntheticActivity,
		formatValidationMessage,
		getActivityDescriptor,
		getActivityTemplates,
		getCourseBucket as resolveDisplayCourseBucket,
		getCourseDisplayCode,
		getCourseDisplayName,
		getCourseExaminerMode,
		getCourseGradingMode,
		getCourseSortKey,
		getCourseTooltip,
		getProgrammeLabel,
		getTimeRangeParts,
		isActivityAllowedForProgramme,
		isCourseBucket as courseIsBucket,
		normalizeCourseCode,
		recategorizePlanCourses,
		resolveCourseBucketForRequirements
	} from '$lib/planner-ui';

	type DragPayload =
		| { kind: 'catalog'; courseCode: string }
		| { kind: 'planned'; courseCode: string; semester: number };

	type SemesterView = {
		semester: number;
		courses: PlannedCourse[];
		totalEcts: number;
		validation?: SemesterValidationResult;
		blocks: Map<string, PlannedCourse[]>;
		conflictedBlocks: Set<string>;
		conflictedCourses: Set<string>;
	};

	type DragCoursePreview = {
		courseCode: string;
		displayName: string;
		title: string;
		timeBlocks: string[];
	};

	const initialVolume = String(new Date().getFullYear());

	let availableCourses = $state<CourseSummary[]>([]);
	let plan = $state<StudyPlan>({ courses: [] });
	let planValidation = $state<PlanValidationResult | null>(null);
	let courseCodesInput = $state(DEFAULT_CODES);
	let courseFilter = $state('');
	let volume = $state(initialVolume);
	let studyVolume = $state(initialVolume);
	let status = $state('');
	let selectedSemester = $state(1);
	let semesterCount = $state(6);
	let loading = $state(false);
	let programmesLoading = $state(false);
	let draggedCourse = $state<DragCoursePreview | null>(null);
	let hoveredSemester = $state<number | null>(null);
	let selectedPlacementByCourseCode = $state<Record<string, string>>({});
	let availableProgrammes = $state<ProgrammeListItem[]>([]);
	let selectedProgrammeCode = $state('');
	let selectedProgrammeDefinition = $state<ProgrammeDefinitionResponse | null>(null);
	let selectedStudyFlowOptionId = $state('');
	let loadedProgrammesVolume = $state<number | null>(null);
	let loadedCoursesVolume = $state<string | null>(null);
	let courseCacheByCode = $state<Record<string, CourseSummary>>({});
	let missingCourseCache = $state<Record<string, true>>({});
	let loadPlanInput = $state<HTMLInputElement | null>(null);
	let toastVisible = $state(false);
	let statusTimeout: ReturnType<typeof setTimeout> | null = null;
	let loadedProgrammeDefinitionKey = $state('');
	let selectedActivityType = $state('');
	let selectedActivityEcts = $state('');
	let dismissedPlanConflictKey = $state('');
	let placementPickerCourseCode = $state<string | null>(null);
	let showHosExtraInfo = $state(false);

	const studyFlowOptions = $derived(selectedProgrammeDefinition?.studyFlowOptions ?? []);
	const mandatoryCourseCodes = $derived.by(
		() =>
			new Set(
				(selectedProgrammeDefinition?.mandatoryCourses ?? []).map((entry) =>
					normalizeCourseCode(entry.course.courseCode)
				)
			)
	);
	const programmeBucketByCourseCode = $derived.by(() => {
		const entries = selectedProgrammeDefinition?.mandatoryCourses ?? [];
		return Object.fromEntries(
			entries.map((entry) => [normalizeCourseCode(entry.course.courseCode), entry.bucket])
		) as Record<string, string>;
	});
	const legendItems = $derived.by(() => {
		if (!selectedProgrammeDefinition) {
			return [];
		}

		if (selectedProgrammeDefinition.programme.level === 'beng') {
			return [
				{ bucket: 'mandatory', label: 'Mandatory' },
				{ bucket: 'electives', label: 'Electives' },
				{ bucket: 'internship', label: 'Internship' },
				{ bucket: 'projects', label: 'Diploma project' }
			];
		}

		return [
			{ bucket: 'polytechnicalFoundation', label: 'Polytechnical Foundation' },
			{ bucket: 'programmeSpecific', label: 'Programme Specific' },
			{ bucket: 'projects', label: 'Projects' },
			{ bucket: 'electives', label: 'Electives' }
		];
	});
	const groupedProgrammes = $derived.by(() => {
		const groups = [
			{ key: 'bsc', label: 'Bachelor of Science', programmes: [] as ProgrammeListItem[] },
			{ key: 'beng', label: 'Bachelor of Engineering', programmes: [] as ProgrammeListItem[] },
			{ key: 'msc', label: 'Master of Science', programmes: [] as ProgrammeListItem[] }
		];

		for (const programme of availableProgrammes) {
			const group = groups.find((candidate) => candidate.key === programme.level);
			if (group) {
				group.programmes.push(programme);
			}
		}

		for (const group of groups) {
			group.programmes.sort((left, right) =>
				getProgrammeLabel(left).localeCompare(getProgrammeLabel(right), undefined, {
					sensitivity: 'base'
				})
			);
		}

		return groups.filter((group) => group.programmes.length > 0);
	});
	const activityTemplates = $derived.by(() =>
		getActivityTemplates(selectedProgrammeDefinition?.programme.level ?? null)
	);
	const selectedActivityTemplate = $derived.by(
		() => activityTemplates.find((template) => template.id === selectedActivityType) ?? null
	);
	const requirementBars = $derived.by(() =>
		buildRequirementBars(
			plan.courses,
			selectedProgrammeDefinition?.programme.level ?? null,
			selectedProgrammeDefinition?.bucketLimits,
			mandatoryCourseCodes,
			programmeBucketByCourseCode
		)
	);
	const examRuleBars = $derived.by(() =>
		buildExamRuleBars(plan.courses, selectedProgrammeDefinition?.bucketLimits)
	);
	const planConflictKey = $derived.by(() =>
		(planValidation?.planConflicts ?? [])
			.map((conflict) => `${conflict.message}|${conflict.conflictingCourseCodes.join(',')}`)
			.join('||')
	);
	const visiblePlanConflicts = $derived.by(() =>
		planConflictKey && planConflictKey !== dismissedPlanConflictKey
			? (planValidation?.planConflicts ?? [])
			: []
	);

	const filteredCourses = $derived.by(() =>
		availableCourses
			.filter((course) => courseMatchesQuery(course, courseFilter))
			.toSorted(
				(left, right) =>
					Number(hasCourse(left.courseCode)) - Number(hasCourse(right.courseCode)) ||
					getCourseSortKey(left).localeCompare(getCourseSortKey(right), undefined, {
						numeric: true,
						sensitivity: 'base'
					})
			)
	);
	const semesters = $derived.by(() =>
		Array.from({ length: semesterCount }, (_, index) => index + 1)
	);

	const semesterViews = $derived.by(() =>
		semesters.map((semester) => {
			const courses = plan.courses.filter((course) => course.semester === semester);
			const validation = planValidation?.semesters.find((entry) => entry.semester === semester);
			const blocks = new SvelteMap<string, PlannedCourse[]>();
			const conflictedBlocks = new SvelteSet<string>();
			const conflictedCourses = new SvelteSet<string>();

			for (const course of courses) {
				for (const block of course.timeBlocks) {
					const items = blocks.get(block) ?? [];
					items.push(course);
					blocks.set(block, items);
				}
			}

			for (const conflict of validation?.conflicts ?? []) {
				for (const code of conflict.conflictingCourseCodes) conflictedCourses.add(code);
				for (const block of conflict.sharedTimeBlocks) conflictedBlocks.add(block);
			}

			return {
				semester,
				courses,
				totalEcts:
					validation?.totalEcts ?? courses.reduce((sum, course) => sum + (course.ects ?? 0), 0),
				validation,
				blocks,
				conflictedBlocks,
				conflictedCourses
			};
		})
	);

	onMount(() => {
		void (async () => {
			setStatus('Loading starter courses...');
			await loadProgrammes();
			await loadCourses();
			await refreshPlanValidation();
		})();

		return () => {
			if (statusTimeout) {
				clearTimeout(statusTimeout);
			}
		};
	});

	$effect(() => {
		const volumeNumber = parseStudyVolumeNumber();
		const key =
			selectedProgrammeCode && volumeNumber !== null
				? `${selectedProgrammeCode}:${volumeNumber}`
				: '';

		if (!key) {
			loadedProgrammeDefinitionKey = '';
			if (!selectedProgrammeCode) {
				selectedProgrammeDefinition = null;
				selectedStudyFlowOptionId = '';
			}
			return;
		}

		if (loadedProgrammeDefinitionKey === key) {
			return;
		}

		loadedProgrammeDefinitionKey = key;
		void loadProgrammeDefinition(selectedProgrammeCode);
	});

	$effect(() => {
		if (!showHosExtraInfo && studyVolume !== volume) {
			studyVolume = volume;
		}
	});

	$effect(() => {
		if (!selectedActivityType) {
			return;
		}

		if (!activityTemplates.some((template) => template.id === selectedActivityType)) {
			selectedActivityType = '';
			selectedActivityEcts = '';
		}
	});

	$effect(() => {
		if (!planConflictKey) {
			dismissedPlanConflictKey = '';
		}
	});

	$effect(() => {
		if (selectedSemester > semesterCount) {
			selectedSemester = semesterCount;
		}
	});

	function setStatus(message: string) {
		status = message;
		toastVisible = false;

		if (statusTimeout) {
			clearTimeout(statusTimeout);
		}

		requestAnimationFrame(() => {
			toastVisible = true;
			statusTimeout = setTimeout(() => {
				toastVisible = false;
			}, 3000);
		});
	}

	function dismissPlanConflictToast() {
		dismissedPlanConflictKey = planConflictKey;
	}

	function formatPlanConflictSummary(conflict: PlacementResult) {
		if (conflict.conflictingCourseCodes.length === 0) {
			return conflict.message;
		}

		return `${conflict.message} ${conflict.conflictingCourseCodes.join(', ')}`;
	}

	function hasSemester(semester: number) {
		return semester >= 1 && semester <= semesterCount;
	}

	function addSemester() {
		semesterCount += 1;
		setStatus(`Added semester ${semesterCount}.`);
	}

	async function removeSemester() {
		if (semesterCount <= 1) {
			setStatus('At least one semester must remain in the planner.');
			return;
		}

		const lastSemesterCourses = plan.courses.filter((course) => course.semester === semesterCount);
		if (lastSemesterCourses.length > 0) {
			setStatus(
				`Semester ${semesterCount} still contains ${lastSemesterCourses.length} placed course${lastSemesterCourses.length === 1 ? '' : 's'}. Remove them before deleting the semester.`
			);
			return;
		}

		semesterCount -= 1;
		if (selectedSemester > semesterCount) {
			selectedSemester = semesterCount;
		}
		await refreshPlanValidation();
		setStatus(`Removed semester ${semesterCount + 1}.`);
	}

	function buildValidationContext() {
		return {
			programmeLevel: selectedProgrammeDefinition?.programme.level ?? null,
			approvedMscElectiveCourseCodes:
				selectedProgrammeDefinition?.approvedMscElectiveCourseCodes ?? [],
			mandatoryCourseCodes: [...mandatoryCourseCodes],
			bucketLimits: selectedProgrammeDefinition?.bucketLimits ?? null
		};
	}

	async function refreshPlanValidation() {
		planValidation = await validatePlan(plan, buildValidationContext());
	}

	function parseStudyVolumeNumber() {
		const parsed = Number.parseInt(studyVolume, 10);
		return Number.isFinite(parsed) ? parsed : null;
	}

	function getImportedActivities() {
		return availableCourses.filter((course) => isSyntheticActivity(course));
	}

	function buildAvailableCourses(courses: CourseSummary[], activities = getImportedActivities()) {
		availableCourses = [...courses, ...activities];
	}

	function resetCourseCache(nextVolume?: string) {
		availableCourses = [];
		courseCacheByCode = {};
		missingCourseCache = {};
		loadedCoursesVolume = nextVolume ?? null;
	}

	function buildAvailableCoursesFromCodes(codes: string[]) {
		const courses = codes
			.map((code) => courseCacheByCode[normalizeCourseCode(code)])
			.filter((course): course is CourseSummary => Boolean(course));
		buildAvailableCourses(courses);
	}

	async function fetchCourseBatch(codes: string[]) {
		return fetchCourseBatchFromApi(volume, codes);
	}

	async function loadProgrammes() {
		const volumeNumber = parseStudyVolumeNumber();
		if (volumeNumber === null) {
			availableProgrammes = [];
			selectedProgrammeDefinition = null;
			selectedProgrammeCode = '';
			selectedStudyFlowOptionId = '';
			loadedProgrammeDefinitionKey = '';
			loadedProgrammesVolume = null;
			return;
		}

		if (loadedProgrammesVolume === volumeNumber && availableProgrammes.length > 0) {
			return;
		}

		programmesLoading = true;

		try {
			const payload = await fetchProgrammes(volumeNumber);
			availableProgrammes = payload.programmes;
			loadedProgrammesVolume = volumeNumber;

			if (
				selectedProgrammeCode &&
				!availableProgrammes.some((programme) => programme.code === selectedProgrammeCode)
			) {
				selectedProgrammeCode = '';
				selectedProgrammeDefinition = null;
				selectedStudyFlowOptionId = '';
				loadedProgrammeDefinitionKey = '';
			}
		} catch (error) {
			console.error(error);
			availableProgrammes = [];
			selectedProgrammeDefinition = null;
			selectedStudyFlowOptionId = '';
			loadedProgrammeDefinitionKey = '';
			loadedProgrammesVolume = null;
			setStatus(`Programme loading failed: ${error}`);
		} finally {
			programmesLoading = false;
		}
	}

	async function loadProgrammeDefinition(programmeCode: string) {
		const volumeNumber = parseStudyVolumeNumber();
		if (!programmeCode || volumeNumber === null) {
			selectedProgrammeDefinition = null;
			selectedStudyFlowOptionId = '';
			loadedProgrammeDefinitionKey = '';
			return;
		}

		programmesLoading = true;

		try {
			const payload = await fetchProgrammeDefinition(programmeCode, volumeNumber);
			selectedProgrammeDefinition = payload;
			selectedStudyFlowOptionId = '';
			loadedProgrammeDefinitionKey = `${programmeCode}:${volumeNumber}`;
			const nextProgrammeLevel = payload.programme.level;
			availableCourses = availableCourses.filter(
				(course) =>
					!isSyntheticActivity(course) || isActivityAllowedForProgramme(course, nextProgrammeLevel)
			);
			if (plan.courses.length > 0) {
				plan = {
					courses: recategorizePlanCourses(
						plan.courses.filter((course) =>
							isActivityAllowedForProgramme(course, nextProgrammeLevel)
						),
						nextProgrammeLevel,
						new Set(
							payload.mandatoryCourses.map((entry) => normalizeCourseCode(entry.course.courseCode))
						),
						Object.fromEntries(
							payload.mandatoryCourses.map((entry) => [
								normalizeCourseCode(entry.course.courseCode),
								entry.bucket
							])
						)
					)
				};
			}
			await refreshPlanValidation();
		} catch (error) {
			console.error(error);
			selectedProgrammeDefinition = null;
			selectedStudyFlowOptionId = '';
			loadedProgrammeDefinitionKey = '';
			setStatus(`Programme package loading failed: ${error}`);
		} finally {
			programmesLoading = false;
		}
	}

	async function loadCourses() {
		const codes = parseCourseCodes(courseCodesInput);
		if (!codes.length) {
			buildAvailableCourses([], getImportedActivities());
			setStatus('Enter at least one course code.');
			return;
		}

		if (loadedCoursesVolume !== volume) {
			resetCourseCache(volume);
		}

		const normalizedCodes = codes.map(normalizeCourseCode);
		const codesToFetch = normalizedCodes.filter(
			(code) => !courseCacheByCode[code] && !missingCourseCache[code]
		);

		loading = true;
		try {
			const payload = await fetchCourseBatch(codesToFetch);

			if (payload.courses.length > 0) {
				courseCacheByCode = {
					...courseCacheByCode,
					...Object.fromEntries(
						payload.courses.map((course) => [normalizeCourseCode(course.courseCode), course])
					)
				};
			}

			if (payload.missingCourseCodes.length > 0) {
				missingCourseCache = {
					...missingCourseCache,
					...Object.fromEntries(
						payload.missingCourseCodes.map((code) => [normalizeCourseCode(code), true])
					)
				};
			}

			buildAvailableCoursesFromCodes(normalizedCodes);

			const visibleMissingCourseCodes = normalizedCodes.filter((code) =>
				Boolean(missingCourseCache[code])
			);
			const loadedMessage =
				codesToFetch.length > 0
					? `Loaded ${codesToFetch.length} new course${codesToFetch.length === 1 ? '' : 's'}; ${availableCourses.length} course${availableCourses.length === 1 ? '' : 's'} available.`
					: `Reused ${availableCourses.length} cached course${availableCourses.length === 1 ? '' : 's'}.`;

			setStatus(
				visibleMissingCourseCodes.length > 0
					? `${loadedMessage} Missing course code${visibleMissingCourseCodes.length === 1 ? '' : 's'}: ${visibleMissingCourseCodes.join(', ')}.`
					: loadedMessage
			);
		} catch (error) {
			console.error(error);
			setStatus(`Course loading failed: ${error}`);
		} finally {
			loading = false;
		}
	}

	async function fetchCoursesByCodes(codes: string[]) {
		if (!codes.length) {
			resetCourseCache(volume);
			return [];
		}

		if (loadedCoursesVolume !== volume) {
			resetCourseCache(volume);
		}

		const normalizedCodes = codes.map(normalizeCourseCode);
		const codesToFetch = normalizedCodes.filter(
			(code) => !courseCacheByCode[code] && !missingCourseCache[code]
		);
		const payload = await fetchCourseBatch(codesToFetch);

		if (payload.courses.length > 0) {
			courseCacheByCode = {
				...courseCacheByCode,
				...Object.fromEntries(
					payload.courses.map((course) => [normalizeCourseCode(course.courseCode), course])
				)
			};
		}

		if (payload.missingCourseCodes.length > 0) {
			missingCourseCache = {
				...missingCourseCache,
				...Object.fromEntries(
					payload.missingCourseCodes.map((code) => [normalizeCourseCode(code), true])
				)
			};
		}

		buildAvailableCoursesFromCodes(normalizedCodes);
		return normalizedCodes.filter((code) => Boolean(missingCourseCache[code]));
	}

	async function applySavedPlan(savedPlan: SavedStudyPlan) {
		const savedPlacementSelections = Object.fromEntries(
			Object.entries(savedPlan.selectedPlacementByCourseCode).filter(
				([, value]) => typeof value === 'string'
			)
		);
		const planCourseSelections = Object.fromEntries(
			savedPlan.plan.courses
				.filter(
					(course) =>
						typeof course.placementOptionId === 'string' && course.placementOptionId.length > 0
				)
				.map((course) => [course.courseCode, course.placementOptionId as string])
		);
		const importedCourseCodes =
			savedPlan.importedCourseCodes.length > 0
				? savedPlan.importedCourseCodes
				: [...new Set(savedPlan.plan.courses.map((course) => course.courseCode))];
		const importedRealCourseCodes = importedCourseCodes.filter(
			(code) => !code.startsWith('activity:')
		);
		const importedActivities = (savedPlan.importedActivities ?? [])
			.filter((course) => isSyntheticActivity(course))
			.map((course) => ({
				...course,
				gradingMode: getCourseGradingMode(course),
				examinerMode: getCourseExaminerMode(course)
			}));

		volume = savedPlan.volume;
		studyVolume = savedPlan.volume;
		semesterCount = Math.max(
			6,
			savedPlan.semesterCount ?? 0,
			...savedPlan.plan.courses.map((course) => course.semester)
		);
		courseCodesInput = importedRealCourseCodes.join(',');
		selectedPlacementByCourseCode = {
			...planCourseSelections,
			...savedPlacementSelections
		};
		selectedActivityType = '';
		selectedActivityEcts = '';

		await loadProgrammes();
		const missingCourseCodes = await fetchCoursesByCodes(importedRealCourseCodes);
		plan = {
			courses: savedPlan.plan.courses.map((course) => {
				const catalogCourse = courseCacheByCode[normalizeCourseCode(course.courseCode)];
				const gradingMode =
					course.gradingMode === 'graded' || course.gradingMode === 'passFail'
						? course.gradingMode
						: catalogCourse?.gradingMode;
				const examinerMode =
					course.examinerMode === 'external' || course.examinerMode === 'internal'
						? course.examinerMode
						: catalogCourse?.examinerMode;

				return {
					...course,
					timeBlocks: Array.isArray(course.timeBlocks) ? [...course.timeBlocks] : [],
					gradingMode: getCourseGradingMode({ ...course, gradingMode }),
					examinerMode: getCourseExaminerMode({ ...course, examinerMode }),
					bucket:
						course.bucket ??
						catalogCourse?.bucket ??
						resolveCourseBucketForRequirements(
							course,
							selectedProgrammeDefinition?.programme.level ?? null,
							mandatoryCourseCodes,
							programmeBucketByCourseCode
						)
				};
			})
		};
		buildAvailableCourses(
			importedRealCourseCodes
				.map((code) => courseCacheByCode[normalizeCourseCode(code)])
				.filter((course): course is CourseSummary => Boolean(course)),
			importedActivities
		);
		await refreshPlanValidation();

		return {
			missingCourseCodes,
			placedCourseCount: plan.courses.length
		};
	}

	function buildSavedStudyPlan(): SavedStudyPlan {
		return buildSavedStudyPlanPayload({
			availableCourses,
			plan,
			volume,
			semesterCount,
			selectedPlacementByCourseCode
		});
	}

	function savePlan() {
		const payload = buildSavedStudyPlan();
		const blob = new Blob([JSON.stringify(payload, null, 2)], { type: 'application/json' });
		const downloadUrl = URL.createObjectURL(blob);
		const anchor = document.createElement('a');
		const timestamp = payload.savedAt.slice(0, 10);

		anchor.href = downloadUrl;
		anchor.download = `dtu-study-plan-${timestamp}.json`;
		anchor.click();
		URL.revokeObjectURL(downloadUrl);

		setStatus(
			`Saved study plan with ${payload.plan.courses.length} placed course${payload.plan.courses.length === 1 ? '' : 's'}.`
		);
	}

	function openLoadPlanDialog() {
		loadPlanInput?.click();
	}

	async function handleLoadPlanFile(event: Event) {
		const input = event.currentTarget as HTMLInputElement;
		const file = input.files?.[0];

		if (!file) {
			return;
		}

		loading = true;

		try {
			const text = await file.text();
			const parsed = JSON.parse(text) as unknown;

			if (!isSavedStudyPlan(parsed)) {
				throw new Error('The selected file is not a valid study plan export.');
			}

			const { missingCourseCodes, placedCourseCount } = await applySavedPlan(parsed);
			setStatus(
				missingCourseCodes.length > 0
					? `Loaded study plan with ${placedCourseCount} placed course${placedCourseCount === 1 ? '' : 's'}. Missing course code${missingCourseCodes.length === 1 ? '' : 's'}: ${missingCourseCodes.join(', ')}.`
					: `Loaded study plan with ${placedCourseCount} placed course${placedCourseCount === 1 ? '' : 's'}.`
			);
		} catch (error) {
			console.error(error);
			setStatus(`Plan loading failed: ${error}`);
		} finally {
			loading = false;
			input.value = '';
		}
	}

	function hasCourse(courseCode: string) {
		return plan.courses.some((course) => course.courseCode === courseCode);
	}

	function hasImportedCourse(courseCode: string) {
		return availableCourses.some((course) => course.courseCode === courseCode);
	}

	function plannedCourseIsMissingFromImport(course: PlannedCourse) {
		return !isSyntheticActivity(course) && !hasImportedCourse(course.courseCode);
	}

	function hasImportedActivity(activityType: string) {
		return availableCourses.some(
			(course) => isSyntheticActivity(course) && course.activityType === activityType
		);
	}

	function getProjectFillTimeBlocks(semester: number) {
		const prefix = semester % 2 === 1 ? 'E' : 'F';
		return [
			`${prefix}1A`,
			`${prefix}1B`,
			`${prefix}2A`,
			`${prefix}2B`,
			`${prefix}3A`,
			`${prefix}3B`,
			`${prefix}4A`,
			`${prefix}4B`,
			`${prefix}5A`,
			`${prefix}5B`
		];
	}

	function getProjectedActivityBlocks(view: SemesterView, course: PlannedCourse) {
		if (!isSyntheticActivity(course) || course.scheduleMode !== 'freeFill') {
			return [];
		}

		const occupiedBlocks = new Set(
			view.courses
				.filter(
					(entry) => entry.courseCode !== course.courseCode && entry.scheduleMode !== 'freeFill'
				)
				.flatMap((entry) => entry.timeBlocks)
				.map((block) => block.toUpperCase())
		);

		return getProjectFillTimeBlocks(view.semester).filter((block) => !occupiedBlocks.has(block));
	}

	function buildPlannedCoursesForPlacement(
		course: CourseSummary,
		semester: number
	): PlannedCourse[] {
		if (isSyntheticActivity(course)) {
			return [
				{
					courseCode: course.courseCode,
					title: course.title,
					courseLevel: course.courseLevel,
					ects: course.ects,
					semester,
					timeBlocks: [],
					kind: course.kind,
					activityType: course.activityType,
					displayCode: course.displayCode,
					scheduleMode: course.scheduleMode,
					bucket: getCourseBucket(course),
					gradingMode: getCourseGradingMode(course),
					examinerMode: getCourseExaminerMode(course)
				}
			];
		}

		const selectedPlacement = getSelectedPlacementOption(
			course,
			selectedPlacementByCourseCode[course.courseCode]
		);
		const sourceTimeBlocks = selectedPlacement?.timeBlocks ?? course.timeBlocks;

		if (isMultiSemesterSpringStartCourse(course.courseCode)) {
			return [
				{
					courseCode: course.courseCode,
					title: course.title,
					courseLevel: course.courseLevel,
					ects: 0,
					semester,
					placementOptionId: selectedPlacement?.id,
					placementOptionLabel: selectedPlacement?.label,
					timeBlocks: getEvenSemesterBlocks(sourceTimeBlocks),
					kind: course.kind,
					activityType: course.activityType,
					displayCode: course.displayCode,
					scheduleMode: course.scheduleMode ?? 'fixed',
					bucket: getCourseBucket(course),
					gradingMode: getCourseGradingMode(course),
					examinerMode: getCourseExaminerMode(course)
				},
				{
					courseCode: course.courseCode,
					title: course.title,
					courseLevel: course.courseLevel,
					ects: course.ects,
					semester: semester + 1,
					placementOptionId: selectedPlacement?.id,
					placementOptionLabel: selectedPlacement?.label,
					timeBlocks: getOddSemesterBlocks(sourceTimeBlocks),
					kind: course.kind,
					activityType: course.activityType,
					displayCode: course.displayCode,
					scheduleMode: course.scheduleMode ?? 'fixed',
					bucket: getCourseBucket(course),
					gradingMode: getCourseGradingMode(course),
					examinerMode: getCourseExaminerMode(course)
				}
			];
		}

		const resolvedTimeBlocks =
			selectedPlacement?.timeBlocks ?? getSemesterSpecificTimeBlocks(course.timeBlocks, semester);

		return [
			{
				courseCode: course.courseCode,
				title: course.title,
				courseLevel: course.courseLevel,
				ects: course.ects,
				semester,
				placementOptionId: selectedPlacement?.id,
				placementOptionLabel: selectedPlacement?.label,
				timeBlocks: resolvedTimeBlocks,
				kind: course.kind,
				activityType: course.activityType,
				displayCode: course.displayCode,
				scheduleMode: course.scheduleMode ?? 'fixed',
				bucket: getCourseBucket(course),
				gradingMode: getCourseGradingMode(course),
				examinerMode: getCourseExaminerMode(course)
			}
		];
	}

	async function validateCandidates(planToCheck: StudyPlan, candidates: PlannedCourse[]) {
		let workingPlan = planToCheck;

		for (const candidate of candidates) {
			const result = await validatePlacement(workingPlan, candidate, buildValidationContext());

			if (!result.allowed) {
				return result;
			}

			workingPlan = {
				courses: [...workingPlan.courses, candidate]
			};
		}

		return null;
	}

	async function addCourse(course: CourseSummary, semester: number) {
		const courseName = getCourseDisplayName(course);

		if (hasCourse(course.courseCode)) {
			setStatus(`${courseName} is already in the plan.`);
			return;
		}

		if (!isSyntheticActivity(course) && isMultiSemesterSpringStartCourse(course.courseCode)) {
			if (semester % 2 === 1) {
				setStatus(
					`${courseName} must start in an even semester and continue in the following odd semester.`
				);
				selectedSemester = semester;
				return;
			}

			if (!hasSemester(semester + 1)) {
				setStatus(`${courseName} needs the following odd semester to exist in the plan.`);
				selectedSemester = semester;
				return;
			}
		}

		const candidates = buildPlannedCoursesForPlacement(course, semester);
		const result = await validateCandidates(plan, candidates);

		if (result) {
			selectedSemester = semester;
			setStatus(formatValidationMessage(result));
			return;
		}

		plan.courses = [...plan.courses, ...candidates];
		selectedSemester = semester;
		await refreshPlanValidation();
		setStatus(
			!isSyntheticActivity(course) && isMultiSemesterSpringStartCourse(course.courseCode)
				? `Added ${courseName} across semesters ${semester} and ${semester + 1}.`
				: `Added ${courseName} to semester ${semester}.`
		);
	}

	async function moveCourse(course: PlannedCourse, semester: number) {
		const sourceCourse = availableCourses.find((entry) => entry.courseCode === course.courseCode);
		if (!sourceCourse) return;
		const courseName = getCourseDisplayName(course);

		const targetStartSemester =
			!isSyntheticActivity(course) &&
			isMultiSemesterSpringStartCourse(course.courseCode) &&
			course.semester % 2 === 1
				? semester - 1
				: semester;

		if (
			targetStartSemester ===
			(!isSyntheticActivity(course) &&
			isMultiSemesterSpringStartCourse(course.courseCode) &&
			course.semester % 2 === 1
				? course.semester - 1
				: course.semester)
		) {
			return;
		}

		if (!isSyntheticActivity(course) && isMultiSemesterSpringStartCourse(course.courseCode)) {
			if (targetStartSemester % 2 === 1) {
				setStatus(
					`${courseName} must start in an even semester and continue in the following odd semester.`
				);
				selectedSemester = semester;
				return;
			}

			if (!hasSemester(targetStartSemester + 1)) {
				setStatus(`${courseName} needs the following odd semester to exist in the plan.`);
				selectedSemester = semester;
				return;
			}
		}

		const nextPlan = {
			courses: plan.courses.filter((entry) => entry.courseCode !== course.courseCode)
		};
		const candidates = buildPlannedCoursesForPlacement(sourceCourse, targetStartSemester);
		const result = await validateCandidates(nextPlan, candidates);

		if (result) {
			selectedSemester = semester;
			setStatus(`Move blocked. ${formatValidationMessage(result)}`);
			return;
		}

		plan.courses = [...nextPlan.courses, ...candidates];
		selectedSemester = targetStartSemester;
		await refreshPlanValidation();
		setStatus(
			!isSyntheticActivity(course) && isMultiSemesterSpringStartCourse(course.courseCode)
				? `Moved ${courseName} across semesters ${targetStartSemester} and ${targetStartSemester + 1}.`
				: `Moved ${courseName} to semester ${semester}.`
		);
	}

	async function removeCourse(course: PlannedCourse) {
		plan.courses = plan.courses.filter((entry) => entry.courseCode !== course.courseCode);
		await refreshPlanValidation();
		setStatus(`Removed ${getCourseDisplayName(course)}.`);
	}

	function dragCatalog(event: DragEvent, course: CourseSummary) {
		placementPickerCourseCode = null;
		const selectedPlacement = getSelectedPlacementOption(
			course,
			selectedPlacementByCourseCode[course.courseCode]
		);
		const sourceTimeBlocks = selectedPlacement?.timeBlocks ?? course.timeBlocks;

		draggedCourse = {
			courseCode: course.courseCode,
			displayName: getCourseDisplayName(course),
			title: course.title,
			timeBlocks: sourceTimeBlocks
		};

		event.dataTransfer?.setData(
			'text/plain',
			JSON.stringify({ kind: 'catalog', courseCode: course.courseCode })
		);
	}

	function dragPlanned(event: DragEvent, course: PlannedCourse) {
		const sourceCourse = availableCourses.find((entry) => entry.courseCode === course.courseCode);

		draggedCourse = {
			courseCode: course.courseCode,
			displayName: getCourseDisplayName(course),
			title: course.title,
			timeBlocks: sourceCourse?.timeBlocks ?? course.timeBlocks
		};

		event.dataTransfer?.setData(
			'text/plain',
			JSON.stringify({ kind: 'planned', courseCode: course.courseCode, semester: course.semester })
		);
	}

	function clearDragState() {
		draggedCourse = null;
		hoveredSemester = null;
	}

	function getCatalogCourseTimeBlocks(course: CourseSummary) {
		const selectedPlacement = getSelectedPlacementOption(
			course,
			selectedPlacementByCourseCode[course.courseCode]
		);

		return selectedPlacement?.timeBlocks ?? course.timeBlocks;
	}

	function canPlaceCatalogCourseInSemester(course: CourseSummary, semester: number) {
		if (!isSyntheticActivity(course) && isMultiSemesterSpringStartCourse(course.courseCode)) {
			return semester % 2 === 0 && hasSemester(semester + 1);
		}

		return isSemesterCompatible(getCatalogCourseTimeBlocks(course), semester);
	}

	function openPlacementPicker(course: CourseSummary) {
		const courseName = getCourseDisplayName(course);

		if (hasCourse(course.courseCode)) {
			setStatus(`${courseName} is already in the plan.`);
			placementPickerCourseCode = null;
			return;
		}

		placementPickerCourseCode =
			placementPickerCourseCode === course.courseCode ? null : course.courseCode;
	}

	async function placeCourseFromPicker(course: CourseSummary, semester: number) {
		placementPickerCourseCode = null;
		await addCourse(course, semester);
	}

	function canDropIntoSemester(semester: number) {
		if (!draggedCourse) {
			return true;
		}

		const preview = draggedCourse;
		const draggedSource = availableCourses.find((entry) => entry.courseCode === preview.courseCode);
		if (
			draggedSource &&
			!isSyntheticActivity(draggedSource) &&
			isMultiSemesterSpringStartCourse(draggedSource.courseCode)
		) {
			return semester % 2 === 0 && hasSemester(semester + 1);
		}

		return isSemesterCompatible(draggedCourse.timeBlocks, semester);
	}

	function handleDragOver(event: DragEvent, semester: number) {
		if (!canDropIntoSemester(semester)) {
			hoveredSemester = null;
			return;
		}

		event.preventDefault();
		hoveredSemester = semester;
	}

	async function handleDrop(event: DragEvent, semester: number) {
		event.preventDefault();

		if (!canDropIntoSemester(semester)) {
			const courseName = draggedCourse?.displayName ?? 'This course';
			setStatus(`${courseName} cannot be placed in semester ${semester}.`);
			clearDragState();
			return;
		}

		const raw = event.dataTransfer?.getData('text/plain');
		if (!raw) {
			clearDragState();
			return;
		}

		const payload = JSON.parse(raw) as DragPayload;

		if (payload.kind === 'catalog') {
			const course = availableCourses.find((entry) => entry.courseCode === payload.courseCode);
			if (course) await addCourse(course, semester);
			clearDragState();
			return;
		}

		const course = plan.courses.find(
			(entry) => entry.courseCode === payload.courseCode && entry.semester === payload.semester
		);
		if (course) await moveCourse(course, semester);
		clearDragState();
	}

	function handleImport(event: SubmitEvent) {
		event.preventDefault();
		void (async () => {
			await loadProgrammes();
			await loadCourses();
		})();
	}

	function handleActivityTypeChange(activityType: string) {
		selectedActivityType = activityType;
		selectedActivityEcts = '';
	}

	function handleActivityEctsChange(ectsText: string) {
		selectedActivityEcts = ectsText;

		if (!selectedActivityTemplate || !ectsText) {
			return;
		}

		if (
			selectedActivityTemplate.id !== 'specialCourse' &&
			hasImportedActivity(selectedActivityTemplate.id)
		) {
			setStatus(`${selectedActivityTemplate.label} is already in the imported list.`);
			selectedActivityEcts = '';
			return;
		}

		availableCourses = [
			...availableCourses,
			createSyntheticActivity(selectedActivityTemplate, ectsText)
		];
		selectedActivityEcts = '';
		setStatus(
			`Added ${selectedActivityTemplate.title.toLowerCase()} (${ectsText} ECTS) to the imported list.`
		);
	}

	async function handleProgrammeChange(event: Event) {
		const nextCode = (event.currentTarget as HTMLSelectElement).value;
		selectedProgrammeCode = nextCode;
	}

	async function handleStudyFlowOptionChange(event: Event) {
		const nextId = (event.currentTarget as HTMLSelectElement).value;
		selectedStudyFlowOptionId = nextId;

		if (!nextId) {
			return;
		}

		await importRecommendedPackage(nextId);
	}

	async function importRecommendedPackage(studyFlowOptionId = selectedStudyFlowOptionId) {
		if (!selectedProgrammeDefinition || !studyFlowOptionId) {
			setStatus('Choose a programme and recommended package first.');
			return;
		}

		const selectedOption = selectedProgrammeDefinition.studyFlowOptions.find(
			(option) => option.id === studyFlowOptionId
		);

		if (!selectedOption) {
			setStatus('The selected recommended package could not be found.');
			return;
		}

		loading = true;

		try {
			const { missingCourseCodes, placedCourseCount } = await applySavedPlan(
				selectedOption.savedPlan
			);
			setStatus(
				missingCourseCodes.length > 0
					? `Imported ${selectedOption.label} with ${placedCourseCount} placed course${placedCourseCount === 1 ? '' : 's'}. Missing course code${missingCourseCodes.length === 1 ? '' : 's'}: ${missingCourseCodes.join(', ')}.`
					: `Imported ${selectedOption.label} with ${placedCourseCount} placed course${placedCourseCount === 1 ? '' : 's'}.`
			);
		} catch (error) {
			console.error(error);
			setStatus(`Recommended package import failed: ${error}`);
		} finally {
			loading = false;
		}
	}

	async function loadMandatoryCoursesForSelectedProgramme() {
		if (!selectedProgrammeDefinition) {
			setStatus('Choose a programme first.');
			return;
		}

		const mandatoryCourses = selectedProgrammeDefinition.mandatoryCourses.map(
			(entry) => entry.course
		);
		if (mandatoryCourses.length === 0) {
			setStatus('This programme does not currently expose any mandatory courses.');
			return;
		}

		if (loadedCoursesVolume !== volume) {
			resetCourseCache(volume);
		}

		const mandatoryCodes = mandatoryCourses.map((course) => normalizeCourseCode(course.courseCode));

		courseCacheByCode = {
			...courseCacheByCode,
			...Object.fromEntries(
				mandatoryCourses.map((course) => [normalizeCourseCode(course.courseCode), course])
			)
		};

		courseCodesInput = mandatoryCodes.join(',');
		buildAvailableCourses(
			mandatoryCodes
				.map((code) => courseCacheByCode[normalizeCourseCode(code)])
				.filter((course): course is CourseSummary => Boolean(course)),
			[]
		);
		selectedActivityType = '';
		selectedActivityEcts = '';
		selectedPlacementByCourseCode = {};
		plan = { courses: [] };
		await refreshPlanValidation();

		setStatus(
			`Loaded ${mandatoryCodes.length} mandatory course${mandatoryCodes.length === 1 ? '' : 's'} for ${getProgrammeLabel(selectedProgrammeDefinition.programme)} and cleared the current plan.`
		);
	}

	function getCourseBucket(
		course:
			| Pick<CourseSummary, 'courseCode' | 'activityType' | 'kind'>
			| Pick<PlannedCourse, 'courseCode' | 'activityType' | 'kind'>
	) {
		return resolveDisplayCourseBucket(
			course,
			programmeBucketByCourseCode,
			Boolean(selectedProgrammeDefinition)
		);
	}

	function isCourseBucket(
		course:
			| Pick<CourseSummary, 'courseCode' | 'activityType' | 'kind'>
			| Pick<PlannedCourse, 'courseCode' | 'activityType' | 'kind'>,
		bucket: string
	) {
		return courseIsBucket(
			course,
			bucket,
			programmeBucketByCourseCode,
			Boolean(selectedProgrammeDefinition)
		);
	}

	function shownCourseBlocks(course: CourseSummary) {
		if (isSyntheticActivity(course)) {
			return [];
		}

		const selectedPlacement = getSelectedPlacementOption(
			course,
			selectedPlacementByCourseCode[course.courseCode]
		);
		const sourceTimeBlocks = selectedPlacement?.timeBlocks ?? course.timeBlocks;

		if (isMultiSemesterSpringStartCourse(course.courseCode)) {
			return sourceTimeBlocks;
		}

		return sourceTimeBlocks;
	}

	function updateSelectedPlacement(courseCode: string, placementOptionId: string) {
		selectedPlacementByCourseCode = {
			...selectedPlacementByCourseCode,
			[courseCode]: placementOptionId
		};
	}

	function focusSemesterOnKey(event: KeyboardEvent, semester: number) {
		if (event.key === 'Enter' || event.key === ' ') {
			event.preventDefault();
			selectedSemester = semester;
		}
	}

	function semesterBlockCourses(view: SemesterView, block: string) {
		return view.courses.filter((course) =>
			course.timeBlocks.some((timeBlock) => getDisplayBlock(timeBlock) === block)
		);
	}

	function semesterWeeklyCellCourses(view: SemesterView, block: string) {
		const fixedCourses = semesterBlockCourses(view, block);
		if (fixedCourses.length > 0) {
			return fixedCourses;
		}

		return view.courses.filter((course) =>
			getProjectedActivityBlocks(view, course).includes(
				`${view.semester % 2 === 1 ? 'E' : 'F'}${block}`
			)
		);
	}

	function semesterBlockIsConflicted(view: SemesterView, block: string) {
		return [...view.conflictedBlocks].some((timeBlock) => getDisplayBlock(timeBlock) === block);
	}
</script>

<svelte:head><title>DTU Study Planner</title></svelte:head>

<div class="page">
	<header>
		<p class="kicker">DTU study planner</p>
		<h1>Plan courses across DTU semesters</h1>
		<form class="top-controls" onsubmit={handleImport}>
			<div class="top-controls-layout">
				<div class="programme-column">
					<label class="programme-control">
						<span>Programme</span>
						<select
							bind:value={selectedProgrammeCode}
							disabled={programmesLoading}
							onchange={(event) => void handleProgrammeChange(event)}
						>
							<option value="">Choose a programme</option>
							{#each groupedProgrammes as group, index (group.key)}
								<optgroup label={group.label}>
									{#each group.programmes as programme (programme.code)}
										<option value={programme.code}
											>{getProgrammeLabel(programme)} ({programme.code})</option
										>
									{/each}
								</optgroup>
								{#if index < groupedProgrammes.length - 1}
									<option disabled> </option>
									<option disabled>──────────</option>
								{/if}
							{/each}
						</select>
					</label>

					<label class="package-control">
						<span>Recommended course package</span>
						<select
							bind:value={selectedStudyFlowOptionId}
							disabled={programmesLoading ||
								!selectedProgrammeDefinition ||
								studyFlowOptions.length === 0}
							onchange={(event) => void handleStudyFlowOptionChange(event)}
						>
							<option value="">
								{#if !selectedProgrammeCode}
									Choose a programme first
								{:else if programmesLoading}
									Loading packages...
								{:else if studyFlowOptions.length === 0}
									No packages available
								{:else}
									Choose a package
								{/if}
							</option>
							{#each studyFlowOptions as option (option.id)}
								<option value={option.id}>{option.label}</option>
							{/each}
						</select>
					</label>
				</div>

				<div class="import-column">
					<label class="codes-control">
						<span>Load course codes</span>
						<textarea bind:value={courseCodesInput} rows="3"></textarea>
					</label>

					<div class="import-actions-row">
						<button type="submit" disabled={loading}
							>{loading ? 'Loading...' : 'Load courses'}</button
						>
						<label class="volume-control">
							<span>Course volume</span>
							<input bind:value={volume} />
						</label>
						{#if showHosExtraInfo}
							<label class="volume-control">
								<span>Study volume</span>
								<input bind:value={studyVolume} />
							</label>
						{/if}
					</div>
				</div>

				<div class="thesis-column">
					<div class="activity-controls">
						<label>
							<span>Add activity</span>
							<select
								bind:value={selectedActivityType}
								disabled={!selectedProgrammeDefinition}
								onchange={(event) =>
									handleActivityTypeChange((event.currentTarget as HTMLSelectElement).value)}
							>
								<option value="">
									{#if !selectedProgrammeDefinition}
										Choose a programme first
									{:else}
										Choose an activity
									{/if}
								</option>
								{#each activityTemplates as template (template.id)}
									<option value={template.id}>{template.label}</option>
								{/each}
							</select>
						</label>
						<label>
							<span>ECTS</span>
							<select
								bind:value={selectedActivityEcts}
								disabled={!selectedActivityTemplate}
								onchange={(event) =>
									handleActivityEctsChange((event.currentTarget as HTMLSelectElement).value)}
							>
								<option value="">
									{#if !selectedActivityTemplate}
										Choose an activity first
									{:else}
										Choose ECTS
									{/if}
								</option>
								{#each selectedActivityTemplate?.ectsOptions ?? [] as ectsOption (ectsOption)}
									<option value={ectsOption}>{ectsOption} ECTS</option>
								{/each}
							</select>
						</label>
					</div>

					<div class="actions-control">
						<div class="button-group plan-actions">
							<button type="button" class="secondary-button" onclick={savePlan}>Export plan</button>
							<button type="button" class="secondary-button" onclick={openLoadPlanDialog}
								>Import plan</button
							>
							<button type="button" class="secondary-button" onclick={addSemester}>Add s.</button>
							<button type="button" class="secondary-button" onclick={() => void removeSemester()}
								>Remove s.</button
							>
						</div>
						<button
							type="button"
							class="wide-action"
							disabled={loading ||
								!selectedProgrammeDefinition ||
								selectedProgrammeDefinition.mandatoryCourses.length === 0}
							onclick={() => void loadMandatoryCoursesForSelectedProgramme()}
						>
							Load all mandatory courses
						</button>
						<label class="hos-extra-toggle">
							<input type="checkbox" bind:checked={showHosExtraInfo} />
							<span class="toggle-track" aria-hidden="true">
								<span class="toggle-thumb"></span>
							</span>
							<span class="toggle-label">show HoS extra info</span>
						</label>
					</div>
				</div>
			</div>
			<input
				bind:this={loadPlanInput}
				class="visually-hidden"
				type="file"
				accept="application/json"
				onchange={(event) => void handleLoadPlanFile(event)}
			/>
		</form>
	</header>

	{#if requirementBars.length > 0}
		<section class="panel requirement-ribbon" aria-label="Programme requirement progress">
			{#each requirementBars as bar (bar.bucket)}
				<article
					class={`requirement-pill ${bar.accentClass} ${bar.overflowed ? 'overflowed' : ''}`}
				>
					<div class="requirement-pill-header">
						<span class="requirement-label">{bar.label}</span>
						<span class="requirement-value"
							>{bar.planned.toFixed(1)}/{bar.required.toFixed(1)} ECTS</span
						>
					</div>
					<div class="requirement-track" aria-hidden="true">
						<div class="requirement-capacity" style:width={`${bar.capacityRatio * 100}%`}>
							<div
								class="requirement-fill"
								style:width={`${Math.min(bar.fillRatio / Math.max(bar.capacityRatio, 0.0001), 1) * 100}%`}
							></div>
						</div>
						{#if bar.capacityRatio < 1}
							<div class="requirement-blocked" style:left={`${bar.capacityRatio * 100}%`}></div>
						{/if}
						{#if bar.fulfilled}
							<span class="requirement-check">✓</span>
						{/if}
					</div>
				</article>
			{/each}
		</section>
	{/if}

	{#if showHosExtraInfo && examRuleBars.length > 0}
		<section class="panel requirement-ribbon exam-rule-ribbon" aria-label="Exam rule progress">
			{#each examRuleBars as bar (bar.key)}
				<article
					class={`requirement-pill exam-rule-pill ${bar.accentClass} ${bar.overflowed ? 'overflowed' : ''} ${bar.fulfilled ? 'fulfilled' : ''}`}
				>
					<div class="requirement-pill-header">
						<span class="requirement-label">{bar.label}</span>
						<span class="requirement-value">{bar.valueLabel}</span>
					</div>
					<div class="requirement-track" aria-hidden="true">
						<div class="requirement-capacity" style:width="100%">
							<div
								class="requirement-fill"
								style:width={`${Math.min(bar.fillRatio, 1) * 100}%`}
							></div>
						</div>
						{#if bar.fulfilled || bar.overflowed}
							<span class="requirement-check">{bar.successSymbol}</span>
						{/if}
					</div>
				</article>
			{/each}
		</section>
	{/if}

	<main class="layout">
		<section class="panel imported-panel">
			<h2>Imported courses</h2>
			<label class="panel-filter">
				<span>Filter</span>
				<input bind:value={courseFilter} placeholder="Code or title" />
			</label>
			{#if selectedProgrammeDefinition}
				<div class="classification-legend" aria-label="Course category color legend">
					{#each legendItems as item (item.bucket)}
						<span class="legend-item"
							><span class={`legend-swatch ${item.bucket}`}></span>{item.label}</span
						>
					{/each}
				</div>
			{/if}

			<div class="catalog">
				{#each filteredCourses as course (course.courseCode)}
					<article
						class:used={hasCourse(course.courseCode)}
						class:bucket-polytechnicalFoundation={isCourseBucket(course, 'polytechnicalFoundation')}
						class:bucket-programmeSpecific={isCourseBucket(course, 'programmeSpecific')}
						class:bucket-projects={isCourseBucket(course, 'projects')}
						class:bucket-internship={isCourseBucket(course, 'internship')}
						class:bucket-mandatory={isCourseBucket(course, 'mandatory')}
						class:bucket-electives={isCourseBucket(course, 'electives')}
						class="card"
						draggable="true"
						title={getCourseTooltip(course) || undefined}
						ondragstart={(event) => dragCatalog(event, course)}
						ondragend={clearDragState}
					>
						<button
							type="button"
							class="catalog-card-pick-layer"
							disabled={hasCourse(course.courseCode)}
							aria-label={`Choose a semester for ${getCourseDisplayName(course)}`}
							aria-haspopup="menu"
							aria-expanded={placementPickerCourseCode === course.courseCode}
							onclick={() => openPlacementPicker(course)}
						></button>
						<div class="row card-header">
							<div class="card-title-row">
								{#if getCourseDisplayCode(course)}
									<strong class="course-code">
										<a
											class="course-link"
											href={getCourseDatabaseUrl(course.courseCode)}
											target="_blank"
											rel="noreferrer"
											onclick={(event) => event.stopPropagation()}
										>
											{getCourseDisplayCode(course)}
										</a>
									</strong>
								{/if}
								{#if !isSyntheticActivity(course) && (course.placementOptions?.length ?? 0) > 1}
									<div
										class="placement-switch compact"
										aria-label={`Placement selection for ${course.courseCode}`}
									>
										{#each course.placementOptions ?? [] as option (option.id)}
											<button
												type="button"
												class:selected-option={(selectedPlacementByCourseCode[course.courseCode] ??
													course.selectedPlacementOptionId ??
													course.placementOptions?.[0]?.id) === option.id}
												class="placement-option compact"
												onclick={(event) => {
													event.stopPropagation();
													updateSelectedPlacement(course.courseCode, option.id);
												}}
											>
												{option.id}
											</button>
										{/each}
									</div>
								{/if}
							</div>
							<span>{course.ects ?? 'n/a'} ECTS</span>
						</div>
						<p>{course.title}</p>
						<div class="chips">
							{#if isSyntheticActivity(course)}
								<span class="chip">{getActivityDescriptor(course)}</span>
							{:else}
								{#each sortTimeBlocks(shownCourseBlocks(course)) as block (block)}
									<span class="chip">{block}</span>
								{/each}
							{/if}
						</div>
						{#if placementPickerCourseCode === course.courseCode && !hasCourse(course.courseCode)}
							<div class="catalog-placement-popover" role="menu">
								{#each semesters as semester (semester)}
									<button
										type="button"
										role="menuitem"
										class:current-semester={selectedSemester === semester}
										disabled={!canPlaceCatalogCourseInSemester(course, semester)}
										title={canPlaceCatalogCourseInSemester(course, semester)
											? `Place in semester ${semester}`
											: `Cannot place in semester ${semester}`}
										onclick={(event) => {
											event.stopPropagation();
											void placeCourseFromPicker(course, semester);
										}}
									>
										{semester}
									</button>
								{/each}
							</div>
						{/if}
					</article>
				{/each}
			</div>
		</section>

		<section class="panel planner-panel">
			<div class="row">
				<h2>Study plan</h2>
				<strong
					class:plan-clear={planValidation?.allowed ?? true}
					class:plan-conflict={!(planValidation?.allowed ?? true)}
				>
					{(planValidation?.allowed ?? true) ? 'Plan clear' : 'Conflicts detected'}
				</strong>
			</div>
			<div class="semester-board">
				{#each semesterViews as view (view.semester)}
					<div
						class:selected={selectedSemester === view.semester}
						class:conflict={!(view.validation?.allowed ?? true)}
						class:drag-active={draggedCourse !== null}
						class:drop-allowed={draggedCourse !== null && canDropIntoSemester(view.semester)}
						class:drop-hover={hoveredSemester === view.semester}
						class:drop-disabled={draggedCourse !== null && !canDropIntoSemester(view.semester)}
						class="semester-row-card"
						role="button"
						tabindex="0"
						aria-label={`Select semester ${view.semester}`}
						onclick={() => (selectedSemester = view.semester)}
						onkeydown={(event) => focusSemesterOnKey(event, view.semester)}
						ondragenter={() => {
							if (canDropIntoSemester(view.semester)) hoveredSemester = view.semester;
						}}
						ondragleave={() => {
							if (hoveredSemester === view.semester) hoveredSemester = null;
						}}
						ondragover={(event) => handleDragOver(event, view.semester)}
						ondrop={(event) => void handleDrop(event, view.semester)}
					>
						<div class="semester-row-header">
							<div>
								<strong>Semester {view.semester}</strong>
								<small>{view.totalEcts.toFixed(1)} ECTS</small>
							</div>
							<span
								>{(view.validation?.allowed ?? true)
									? 'OK'
									: describeConflictCount(view.validation?.conflicts.length ?? 0)}</span
							>
						</div>

						<div class="semester-row-body">
							<div class="semester-course-panel">
								{#if view.courses.length === 0}
									<p class="muted empty-copy">Drop a course here.</p>
								{/if}

								{#each view.courses as course (course.courseCode)}
									<div
										class:conflict={view.conflictedCourses.has(course.courseCode)}
										class:missing-import={plannedCourseIsMissingFromImport(course)}
										class:bucket-polytechnicalFoundation={isCourseBucket(
											course,
											'polytechnicalFoundation'
										)}
										class:bucket-programmeSpecific={isCourseBucket(course, 'programmeSpecific')}
										class:bucket-projects={isCourseBucket(course, 'projects')}
										class:bucket-internship={isCourseBucket(course, 'internship')}
										class:bucket-mandatory={isCourseBucket(course, 'mandatory')}
										class:bucket-electives={isCourseBucket(course, 'electives')}
										class="planned"
										role="listitem"
										aria-label={`${getCourseDisplayName(course)} in semester ${view.semester}`}
										title={plannedCourseIsMissingFromImport(course)
											? `${getCourseDisplayName(course)} is not part of the current imported courses list.`
											: undefined}
										draggable="true"
										ondragstart={(event) => dragPlanned(event, course)}
										ondragend={clearDragState}
									>
										<div class="row">
											{#if getCourseDisplayCode(course)}
												<strong class="course-code">
													<a
														class="course-link"
														href={getCourseDatabaseUrl(course.courseCode)}
														target="_blank"
														rel="noreferrer"
													>
														{getCourseDisplayCode(course)}
													</a>
												</strong>
											{:else}
												<strong class="course-code">{course.title}</strong>
											{/if}
											<button
												type="button"
												class="remove-button"
												aria-label={`Remove ${getCourseDisplayName(course)} from the plan`}
												title={`Remove ${getCourseDisplayName(course)}`}
												onclick={() => void removeCourse(course)}
											>
												X
											</button>
										</div>
										{#if course.placementOptionLabel}
											<small>{course.placementOptionLabel}</small>
										{/if}
										{#if plannedCourseIsMissingFromImport(course)}
											<small class="missing-import-note">Not in current imported courses</small>
										{/if}
										{#if getCourseDisplayCode(course)}
											<p>{course.title}</p>
										{/if}
										<div class="chips">
											{#if isSyntheticActivity(course)}
												<span class="chip">{getActivityDescriptor(course)}</span>
											{:else}
												{#each sortTimeBlocks(course.timeBlocks) as block (block)}
													<span class:danger={view.conflictedBlocks.has(block)} class="chip"
														>{block}</span
													>
												{/each}
											{/if}
										</div>
									</div>
								{/each}
							</div>

							<div class="semester-visual">
								<div class="timetable">
									<div class="time-spacer"></div>
									{#each TIMETABLE_DAYS as day, dayIndex (`${day}-${dayIndex}`)}
										<div class="day-label">{day}</div>
									{/each}

									{#each TIMETABLE_TIMES as time (time)}
										{@const timeRange = getTimeRangeParts(time)}
										<div class="time-label">
											<span>{timeRange.start}</span>
											<span class="time-line" aria-hidden="true"></span>
											<span>{timeRange.end}</span>
										</div>
										{#each WEEKLY_TIMETABLE_LAYOUT.filter((cell) => cell.time === time) as cell, cellIndex (`${time}-${cellIndex}`)}
											{@const courses = cell.block
												? semesterWeeklyCellCourses(view, cell.block)
												: []}
											{@const conflicted = cell.block
												? semesterBlockIsConflicted(view, cell.block)
												: false}
											<div
												class:empty={!cell.block}
												class:red={courses.length > 1 || conflicted}
												class:filled={courses.length === 1}
												class="timetable-cell"
											>
												{#if cell.block}
													<strong>{cell.block}</strong>
													{#each courses as course (course.courseCode)}
														<span
															class:bucket-polytechnicalFoundation={isCourseBucket(
																course,
																'polytechnicalFoundation'
															)}
															class:bucket-programmeSpecific={isCourseBucket(
																course,
																'programmeSpecific'
															)}
															class:bucket-projects={isCourseBucket(course, 'projects')}
															class:bucket-internship={isCourseBucket(course, 'internship')}
															class:bucket-mandatory={isCourseBucket(course, 'mandatory')}
															class:bucket-electives={isCourseBucket(course, 'electives')}
															class="badge"
														>
															{getCourseDisplayCode(course) ?? course.title}
														</span>
													{/each}
												{/if}
											</div>
										{/each}
									{/each}
								</div>

								<div class="intensive-panel">
									<h3 class="intensive-panel-title">3w periods</h3>
									<div class="intensive-list">
										{#each getSemesterIntensiveBlocks(view.semester) as block (block)}
											{@const courses = semesterBlockCourses(view, block)}
											{@const conflicted = semesterBlockIsConflicted(view, block)}
											<div
												class:red={courses.length > 1 || conflicted}
												class:filled={courses.length === 1}
												class="cell intensive-cell"
											>
												<strong>{block}</strong>
												{#each courses as course (course.courseCode)}
													<span
														class:bucket-polytechnicalFoundation={isCourseBucket(
															course,
															'polytechnicalFoundation'
														)}
														class:bucket-programmeSpecific={isCourseBucket(
															course,
															'programmeSpecific'
														)}
														class:bucket-projects={isCourseBucket(course, 'projects')}
														class:bucket-internship={isCourseBucket(course, 'internship')}
														class:bucket-mandatory={isCourseBucket(course, 'mandatory')}
														class:bucket-electives={isCourseBucket(course, 'electives')}
														class="badge"
													>
														{getCourseDisplayCode(course) ?? course.title}
													</span>
												{/each}
											</div>
										{/each}
									</div>
								</div>
							</div>
						</div>

						{#if (view.validation?.conflicts.length ?? 0) > 0}
							<div class="warnings semester-warnings">
								{#each view.validation?.conflicts ?? [] as conflict (`${conflict.message}-${conflict.conflictingCourseCodes.join(',')}-${conflict.sharedTimeBlocks.join(',')}`)}
									<div class="warning">
										<strong>{conflict.conflictingCourseCodes.join(' vs ')}</strong>
										<span>{sortTimeBlocks(conflict.sharedTimeBlocks).join(', ')}</span>
									</div>
								{/each}
							</div>
						{/if}
					</div>
				{/each}
			</div>
		</section>
	</main>

	{#if status}
		<div class:visible={toastVisible} class="status-toast" role="status" aria-live="polite">
			{status}
		</div>
	{/if}

	{#if visiblePlanConflicts.length > 0}
		<div class="error-toast" role="alert" aria-live="assertive">
			<div class="error-toast-content">
				{#each visiblePlanConflicts as conflict (`${conflict.message}-${conflict.conflictingCourseCodes.join(',')}`)}
					<p>{formatPlanConflictSummary(conflict)}</p>
				{/each}
			</div>
			<button
				type="button"
				class="error-toast-dismiss"
				onclick={dismissPlanConflictToast}
				aria-label="Dismiss programme requirement warning"
			>
				Dismiss
			</button>
		</div>
	{/if}

	{#if draggedCourse}
		<div class="drag-toast" role="status" aria-live="polite">
			Dragging {draggedCourse.displayName}. Greyed-out semesters are blocked by Fall/Spring parity.
		</div>
	{/if}
</div>
