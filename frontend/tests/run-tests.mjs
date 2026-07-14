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
await transpileLibModule('planner-ui.ts', 'planner-ui.mjs', rewriteLocalImports);
await transpileLibModule('planner-save.ts', 'planner-save.mjs', rewriteLocalImports);

const planner = await import(pathToFileUrl(path.join(buildDir, 'planner.mjs')));
const plannerUi = await import(pathToFileUrl(path.join(buildDir, 'planner-ui.mjs')));
const plannerSave = await import(pathToFileUrl(path.join(buildDir, 'planner-save.mjs')));

test('parseCourseCodes trims empty input chunks', () => {
	assert.deepEqual(planner.parseCourseCodes(' 01001, ,02002 '), ['01001', '02002']);
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
