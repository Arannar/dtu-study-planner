import {
	API_BASE_URL,
	type CoursesResponse,
	type PlanValidationResult,
	type PlacementResult,
	type ProgrammeDefinitionResponse,
	type ProgrammeListResponse,
	type StudyPlan
} from './planner';

export type ValidationContext = {
	programmeLevel: string | null;
	approvedMscElectiveCourseCodes: string[];
	mandatoryCourseCodes: string[];
	bucketLimits: unknown;
};

export async function postJson<T>(path: string, body: unknown): Promise<T> {
	const response = await fetch(`${API_BASE_URL}${path}`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(body)
	});

	if (!response.ok) throw new Error(`HTTP ${response.status} ${response.statusText}`);
	return (await response.json()) as T;
}

export async function fetchCourseBatch(volume: string, codes: string[]): Promise<CoursesResponse> {
	if (!codes.length) {
		return { courses: [], missingCourseCodes: [] };
	}

	const query = new URLSearchParams({ codes: codes.join(','), volume });
	const response = await fetch(`${API_BASE_URL}/api/courses?${query.toString()}`);
	if (!response.ok) throw new Error(`HTTP ${response.status} ${response.statusText}`);
	return (await response.json()) as CoursesResponse;
}

export async function fetchProgrammes(volume: number): Promise<ProgrammeListResponse> {
	const response = await fetch(`${API_BASE_URL}/api/programmes?volume=${volume}`);
	if (!response.ok) throw new Error(`HTTP ${response.status} ${response.statusText}`);
	return (await response.json()) as ProgrammeListResponse;
}

export async function fetchProgrammeDefinition(
	programmeCode: string,
	volume: number,
	language = 'da-DK'
): Promise<ProgrammeDefinitionResponse> {
	const query = new URLSearchParams({ volume: String(volume), language });
	const response = await fetch(
		`${API_BASE_URL}/api/programmes/${encodeURIComponent(programmeCode)}/definition?${query.toString()}`
	);
	if (!response.ok) throw new Error(`HTTP ${response.status} ${response.statusText}`);
	return (await response.json()) as ProgrammeDefinitionResponse;
}

export function validatePlan(
	plan: StudyPlan,
	context: Omit<ValidationContext, 'approvedMscElectiveCourseCodes'>
): Promise<PlanValidationResult> {
	return postJson<PlanValidationResult>('/api/planner/validate-plan', {
		plan,
		...context
	});
}

export function validatePlacement(
	plan: StudyPlan,
	candidate: unknown,
	context: ValidationContext
): Promise<PlacementResult> {
	return postJson<PlacementResult>('/api/planner/validate-placement', {
		plan,
		candidate,
		...context
	});
}
