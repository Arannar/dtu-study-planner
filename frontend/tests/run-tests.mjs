import assert from 'node:assert/strict';
import { mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import path from 'node:path';
import ts from 'typescript';

const root = process.cwd();
const buildDir = path.join(root, '.test-build');

await rm(buildDir, { recursive: true, force: true });
await mkdir(buildDir, { recursive: true });

await transpileLibModule('planner.ts', 'planner.mjs', (source) =>
	source.replace(
		/export const API_BASE_URL =\n\timport\.meta\.env\.VITE_API_BASE_URL \?\? \(import\.meta\.env\.DEV \? 'http:\/\/localhost:5140' : ''\);/,
		"export const API_BASE_URL = '';"
	)
);
await transpileLibModule('planner-api.ts', 'planner-api.mjs', rewriteLocalImports);
await transpileLibModule('planner-ui.ts', 'planner-ui.mjs', rewriteLocalImports);
await transpileLibModule('planner-save.ts', 'planner-save.mjs', rewriteLocalImports);

const planner = await import(pathToFileUrl(path.join(buildDir, 'planner.mjs')));
const plannerUi = await import(pathToFileUrl(path.join(buildDir, 'planner-ui.mjs')));
const plannerSave = await import(pathToFileUrl(path.join(buildDir, 'planner-save.mjs')));

test('parseCourseCodes trims empty input chunks', () => {
	assert.deepEqual(planner.parseCourseCodes(' 01001, ,02002 '), ['01001', '02002']);
});

test('basket merge preserves activities, deduplicates courses and leaves source data unchanged', () => {
	const existing = [
		{ courseCode: '01001', title: 'Existing', timeBlocks: [] },
		{ courseCode: 'activity:test', title: 'Project', kind: 'activity', timeBlocks: [] }
	];
	const result = {
		courseCode: '01002',
		title: 'New',
		timeBlocks: ['E1A'],
		placementOptions: [
			{ id: 'A', label: 'A', timeBlocks: ['E1A'] },
			{ id: 'B', label: 'B', timeBlocks: ['E3A'] }
		]
	};
	const merged = planner.mergeCourseSearchSelection(
		existing,
		[existing[0], result, result],
		['01001', '01002'],
		{ '01002': 'B' },
		'99999,01001'
	);
	assert.equal(merged.courses.length, 3);
	assert.equal(merged.courses[1], existing[1]);
	assert.equal(merged.added.length, 1);
	assert.equal(merged.added[0].selectedPlacementOptionId, 'B');
	assert.equal(result.selectedPlacementOptionId, undefined);
	assert.equal(merged.codesInput, '99999,01001,01002');
	assert.equal(existing.length, 2);
});

test('semester compatibility handles ordinary and intensive blocks', () => {
	assert.equal(planner.isSemesterCompatible(['E1A'], 1), true);
	assert.equal(planner.isSemesterCompatible(['E1A'], 2), false);
	assert.equal(planner.isSemesterCompatible(['JUNE'], 2), true);
	assert.equal(planner.isSemesterCompatible(['JUNE'], 1), false);
});

test('multi-semester block filtering keeps the active semester blocks', () => {
	assert.deepEqual(planner.getSemesterSpecificTimeBlocks(['E1A', 'F1A', 'JUNE'], 2), [
		'F1A',
		'JUNE'
	]);
});

test('saved study plan serialization preserves imported activities and clones arrays', () => {
	const activity = {
		courseCode: 'activity:specialCourse:5:abc',
		title: 'Special course',
		ects: 5,
		timeBlocks: [],
		placementOptions: [],
		kind: 'activity',
		activityType: 'specialCourse'
	};
	const saved = plannerSave.buildSavedStudyPlan({
		availableCourses: [
			{ courseCode: '01001', title: 'Math', ects: 5, timeBlocks: ['E1A'] },
			activity
		],
		plan: {
			courses: [{ courseCode: '01001', title: 'Math', semester: 1, ects: 5, timeBlocks: ['E1A'] }]
		},
		volume: '2026',
		semesterCount: 6,
		selectedPlacementByCourseCode: { '01001': 'A', empty: '' }
	});

	assert.equal(saved.version, 1);
	assert.deepEqual(saved.importedCourseCodes, ['01001']);
	assert.equal(saved.importedActivities.length, 1);
	assert.deepEqual(saved.selectedPlacementByCourseCode, { '01001': 'A' });
	assert.equal(plannerSave.isSavedStudyPlan(saved), true);
	assert.equal(plannerSave.isSavedStudyPlan({ version: 1 }), false);
});

test('requirement bars resolve BSc buckets and elective overflow', () => {
	const bars = plannerUi.buildRequirementBars(
		[
			{
				courseCode: '01001',
				title: 'Foundation',
				semester: 1,
				ects: 55,
				timeBlocks: [],
				bucket: 'polytechnicalFoundation'
			},
			{
				courseCode: '02002',
				title: 'Project',
				semester: 1,
				ects: 30,
				timeBlocks: [],
				bucket: 'projects'
			}
		],
		'bsc',
		{
			totalEcts: 180,
			polytechnicalFoundationEcts: 55,
			programmeSpecificEcts: 55,
			projectsEcts: 25,
			electivesEcts: 45
		},
		new Set(),
		{ '01001': 'polytechnicalFoundation', '02002': 'projects' }
	);

	const foundation = bars.find((bar) => bar.bucket === 'polytechnicalFoundation');
	const projects = bars.find((bar) => bar.bucket === 'projects');
	const electives = bars.find((bar) => bar.bucket === 'electives');
	assert.equal(foundation?.fulfilled, true);
	assert.equal(projects?.overflowed, true);
	assert.equal(electives?.effectiveCapacity, 40);
});

const plannerApi = await import(pathToFileUrl(path.join(buildDir, 'planner-api.mjs')));
const originalFetch = globalThis.fetch;
try {
	let requestedUrl = '';
	globalThis.fetch = async (url) => {
		requestedUrl = String(url);
		return new Response(
			JSON.stringify({
				id: 'view-5302',
				savedPlan: { plan: { courses: [] } },
				missingCourseCodes: []
			}),
			{ status: 200 }
		);
	};
	const option = await plannerApi.fetchStudyFlow('ELEKTEK23', 'view-5302', 2026);
	assert.equal(
		requestedUrl,
		'/api/programmes/ELEKTEK23/study-flows/view-5302?volume=2026&language=da-DK'
	);
	assert.equal(option.savedPlan.plan.courses.length, 0);
	console.log('PASS on-demand study-flow request uses the selected programme, option and volume');
	globalThis.fetch = async () =>
		new Response(JSON.stringify({ detail: 'DTU is unavailable; retry.' }), { status: 503 });
	await assert.rejects(() => plannerApi.fetchCourseBatch('2026', ['01001']), /DTU is unavailable/);
	console.log('PASS DTU failures reject instead of returning missing course codes');
	const originalTimeout = AbortSignal.timeout;
	try {
		AbortSignal.timeout = (milliseconds) => {
			assert.equal(milliseconds, 60_000);
			return AbortSignal.abort(new DOMException('Timed out', 'TimeoutError'));
		};
		globalThis.fetch = async (_url, init) => {
			init.signal.throwIfAborted();
		};
		await assert.rejects(
			() => plannerApi.fetchCourseBatch('2025', ['01001'], true),
			/request timed out.*try loading again/
		);
		console.log('PASS stalled course requests time out with a retryable error');
	} finally {
		AbortSignal.timeout = originalTimeout;
	}
} finally {
	globalThis.fetch = originalFetch;
}

// Exercise the route's real loading lifecycle with controlled pending network requests.
const routeSource = await readFile(path.join(root, 'src/routes/+page.svelte'), 'utf8');
const resetSource = routeSource.slice(
	routeSource.indexOf('\tfunction resetCourseCache('),
	routeSource.indexOf('\tfunction buildAvailableCoursesFromCodes(')
);
const loadSource = routeSource.slice(
	routeSource.indexOf('\tasync function loadCourses('),
	routeSource.indexOf('\tasync function fetchCoursesByCodes(')
);
const makeLoader = new Function(
	'fetchCourseBatch',
	ts.transpileModule(
		`let loading = false, volume = '2025', showHosExtraInfo = true;
		let loadedCoursesVolume = '2026', loadedCoursesWithHistoricalFallback = false;
		let courseCacheByCode = {}, missingCourseCache = {}, status = '';
		let courseCodesInput = '01001';
		let availableCourses = [{ courseCode: '01001', title: 'Previous course' }, { courseCode: 'activity:project' }];
		const parseCourseCodes = (value) => value.split(',');
		const normalizeCourseCode = (value) => value;
		const setStatus = (value) => { status = value; };
		const getImportedActivities = () => availableCourses.filter(c => c.courseCode.startsWith('activity:'));
		const buildAvailableCoursesFromCodes = (codes) => { availableCourses = [...codes.map(c => courseCacheByCode[c]).filter(Boolean), ...getImportedActivities()]; };
		${resetSource}
		${loadSource}
		return { loadCourses, setVolume: value => { volume = value; }, state: () => ({ loading, availableCourses, status }) };`,
		{ compilerOptions: { target: ts.ScriptTarget.ES2022 } }
	).outputText
);
let completeRequest;
const loader = makeLoader(() => new Promise((resolve) => (completeRequest = resolve)));
const pendingLoad = loader.loadCourses();
assert.equal(loader.state().loading, true);
assert.equal(loader.state().availableCourses.length, 2);
completeRequest({
	courses: [{ courseCode: '01001', title: 'Updated course' }],
	missingCourseCodes: []
});
await pendingLoad;
assert.equal(loader.state().loading, false);
assert.equal(loader.state().availableCourses[0].title, 'Updated course');
assert.equal(loader.state().availableCourses[1].courseCode, 'activity:project');
loader.setVolume('2024');
const staleLoad = loader.loadCourses();
loader.setVolume('2023');
completeRequest({ courses: [], missingCourseCodes: ['01001'] });
await staleLoad;
assert.equal(loader.state().loading, false);
assert.equal(loader.state().availableCourses.length, 2);
const failedLoader = makeLoader(async () => {
	throw new Error('Request timed out');
});
await failedLoader.loadCourses();
assert.equal(failedLoader.state().loading, false);
assert.equal(failedLoader.state().availableCourses.length, 2);
assert.match(failedLoader.state().status, /Course loading failed/);
console.log(
	'PASS volume reload preserves courses and activities, rejects stale results, and unlocks after failure'
);

await rm(buildDir, { recursive: true, force: true });

function test(name, assertion) {
	try {
		assertion();
		console.log(`PASS ${name}`);
	} catch (error) {
		console.error(`FAIL ${name}`);
		throw error;
	}
}

async function transpileLibModule(inputName, outputName, transform = (source) => source) {
	const inputPath = path.join(root, 'src', 'lib', inputName);
	const source = transform(await readFile(inputPath, 'utf8'));
	const output = ts.transpileModule(source, {
		compilerOptions: {
			module: ts.ModuleKind.ES2022,
			target: ts.ScriptTarget.ES2022,
			moduleResolution: ts.ModuleResolutionKind.Bundler,
			verbatimModuleSyntax: true
		},
		fileName: inputName
	}).outputText;

	await writeFile(path.join(buildDir, outputName), output, 'utf8');
}

function rewriteLocalImports(source) {
	return source
		.replace(/from '\.\/planner'/g, "from './planner.mjs'")
		.replace(/from '\.\/planner-ui'/g, "from './planner-ui.mjs'")
		.replace(/from '\.\/planner-save'/g, "from './planner-save.mjs'");
}

function pathToFileUrl(filePath) {
	return new URL(`file://${filePath.replaceAll(path.sep, '/')}`).href;
}
