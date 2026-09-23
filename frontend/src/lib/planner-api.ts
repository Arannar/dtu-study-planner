import {
	API_BASE_URL,
	type CoursesResponse,
	type PlanValidationResult,
	type PlacementResult,
	type ProgrammeDefinitionResponse,
	type ProgrammeListResponse,
	type ProgrammeStudyFlowOption,
	type StudyPlan
} from './planner';

export type ValidationContext = {
	programmeLevel: string | null;
	approvedMscElectiveCourseCodes: string[];
	mandatoryCourseCodes: string[];
	bucketLimits: unknown;
};

async function fetchWithTimeout(url: string, init?: RequestInit): Promise<Response> {
	try {
		return await fetch(url, { ...init, signal: AbortSignal.timeout(60_000) });
	} catch (error) {
		if (error instanceof Error && error.name === 'TimeoutError') {
			throw new Error('The request timed out. Please try loading again.', { cause: error });
		}
		throw error;
	}
}

export async function postJson<T>(path: string, body: unknown): Promise<T> {
	const response = await fetchWithTimeout(`${API_BASE_URL}${path}`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(body)
	});

	await ensureSuccess(response);
	return (await response.json()) as T;
}

export async function fetchCourseBatch(
	volume: string,
	codes: string[],
	allowHistoricalFallback = false
): Promise<CoursesResponse> {
	if (!codes.length) {
		return { courses: [], missingCourseCodes: [] };
	}

	const query = new URLSearchParams({ codes: codes.join(','), volume });
	if (allowHistoricalFallback) query.set('allowHistoricalFallback', 'true');
	const response = await fetchWithTimeout(`${API_BASE_URL}/api/courses?${query.toString()}`);
	await ensureSuccess(response);
	return (await response.json()) as CoursesResponse;
}

export async function searchCourses(volume: string, query: string): Promise<CoursesResponse> {
	const parameters = new URLSearchParams({ volume, query });
	const response = await fetchWithTimeout(`${API_BASE_URL}/api/courses/search?${parameters}`);
	await ensureSuccess(response);
	return (await response.json()) as CoursesResponse;
}

export async function fetchProgrammes(volume: number): Promise<ProgrammeListResponse> {
	const response = await fetchWithTimeout(`${API_BASE_URL}/api/programmes?volume=${volume}`);
	await ensureSuccess(response);
	return (await response.json()) as ProgrammeListResponse;
}

export async function fetchProgrammeDefinition(
	programmeCode: string,
	volume: number,
	language = 'da-DK'
): Promise<ProgrammeDefinitionResponse> {
	const query = new URLSearchParams({ volume: String(volume), language });
	const response = await fetchWithTimeout(
		`${API_BASE_URL}/api/programmes/${encodeURIComponent(programmeCode)}/definition?${query.toString()}`
	);
	await ensureSuccess(response);
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

async function ensureSuccess(response: Response): Promise<void> {
	if (response.ok) return;
	const problem = await response.json().catch(() => null);
	throw new Error(
		typeof problem?.detail === 'string'
			? problem.detail
			: `HTTP ${response.status} ${response.statusText}`
	);
}

export async function fetchStudyFlow(
	programmeCode: string,
	optionId: string,
	volume: number,
	language = 'da-DK'
): Promise<ProgrammeStudyFlowOption> {
	const query = new URLSearchParams({ volume: String(volume), language });
	const response = await fetchWithTimeout(
		`${API_BASE_URL}/api/programmes/${encodeURIComponent(programmeCode)}/study-flows/${encodeURIComponent(optionId)}?${query}`
	);
	await ensureSuccess(response);
	return (await response.json()) as ProgrammeStudyFlowOption;
}
