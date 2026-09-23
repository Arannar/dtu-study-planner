import assert from 'node:assert/strict';
import { readFile, mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { build } from 'esbuild';
import { compile } from 'svelte/compiler';
import { Window } from 'happy-dom';

const window = new Window({ url: 'http://localhost/' });
for (const key of [
	'window',
	'document',
	'navigator',
	'MutationObserver',
	'HTMLElement',
	'HTMLMediaElement',
	'HTMLInputElement',
	'HTMLSelectElement',
	'HTMLTextAreaElement',
	'Element',
	'Node',
	'Text',
	'Comment',
	'Event',
	'MouseEvent',
	'CustomEvent',
	'getComputedStyle',
	'requestAnimationFrame',
	'cancelAnimationFrame'
]) {
	Object.defineProperty(globalThis, key, {
		value: key === 'window' ? window : window[key],
		configurable: true
	});
}
const root = process.cwd();
const output = path.join(root, 'node_modules/.cache/course-loading-test');
await mkdir(output, { recursive: true });
const source = (await readFile('src/routes/+page.svelte', 'utf8')).replace(
	"import './+page.css';",
	''
);
const compiled = compile(source, {
	filename: 'src/routes/+page.svelte',
	generate: 'client',
	dev: true
});
await writeFile(path.join(output, 'Page.js'), compiled.js.code);
await build({
	stdin: {
		contents:
			"export {default as Page} from './Page.js'; export {mount, unmount, flushSync} from 'svelte';",
		resolveDir: output
	},
	alias: { $lib: path.join(root, 'src/lib') },
	define: { 'import.meta.env.VITE_API_BASE_URL': "''", 'import.meta.env.DEV': 'true' },
	conditions: ['browser'],
	platform: 'browser',
	bundle: true,
	format: 'esm',
	outfile: path.join(output, 'app.mjs')
});
const requests = [];
let searchMode = 'success';
let releaseSearch;
const searchCourses = [
	{
		courseCode: '02451',
		title: 'Machine Learning',
		ects: 5,
		timeBlocks: ['E1A'],
		placementOptions: []
	},
	{
		courseCode: '01001',
		title: 'Mathematics',
		ects: 10,
		timeBlocks: ['E1A'],
		placementOptions: [
			{ id: 'A', label: 'Scheme A', timeBlocks: ['E1A'] },
			{ id: 'B', label: 'Scheme B', timeBlocks: ['E3A'] }
		]
	},
	{
		courseCode: '01002',
		title: 'More mathematics',
		ects: 5,
		timeBlocks: ['F1A'],
		placementOptions: []
	}
];
globalThis.fetch = async (url) => {
	await new Promise((resolve) => setTimeout(resolve, 5));
	const parsed = new URL(url, 'http://localhost');
	requests.push(parsed.pathname + parsed.search);
	let data;
	if (parsed.pathname === '/api/programmes') data = { programmes: [] };
	else if (parsed.pathname === '/api/planner/validate-plan')
		data = { semesters: [], planConflicts: [] };
	else if (parsed.pathname === '/api/planner/validate-placement')
		data = { allowed: true, conflictingCourseCodes: [], sharedTimeBlocks: [] };
	else if (parsed.pathname === '/api/courses/search') {
		const mode = searchMode;
		if (mode === 'delayed')
			await new Promise((resolve) => {
				releaseSearch = resolve;
			});
		if (mode === 'error')
			return new Response(JSON.stringify({ detail: 'DTU unavailable' }), { status: 503 });
		data = { courses: mode === 'empty' ? [] : searchCourses, missingCourseCodes: [] };
	} else if (parsed.pathname === '/api/courses') {
		const missing = parsed.searchParams.get('volume') === '2024';
		data = {
			courses: missing
				? []
				: [
						{
							courseCode: '02451',
							title: 'Machine Learning',
							ects: 5,
							timeBlocks: ['E1A'],
							placementOptions: []
						}
					],
			missingCourseCodes: missing ? ['02451'] : []
		};
		data.courses.push({
			courseCode: '10060',
			title: 'Physics',
			ects: 10,
			timeBlocks: ['F1A', 'E1A'],
			placementOptions: (missing ? ['A', 'B', 'C', 'A', 'B', 'C', 'D'] : ['A', 'B', 'C', 'D']).map(
				(id) => ({
					id,
					label: `Scheme ${id}`,
					timeBlocks: id === 'B' ? ['F5B', 'E5B'] : ['F1A', 'E1A']
				})
			)
		});
	} else throw new Error(`Unexpected request ${url}`);
	return new Response(JSON.stringify(data), { status: 200 });
};
const { Page, mount, unmount, flushSync } = await import(
	pathToFileURL(path.join(output, 'app.mjs'))
);
const component = mount(Page, { target: document.body });
async function settle() {
	for (let i = 0; i < 20; i++) {
		await new Promise((resolve) => setTimeout(resolve, 5));
		flushSync();
	}
}
function input(element, value) {
	element.value = value;
	element.dispatchEvent(new window.Event('input', { bubbles: true }));
}
try {
	await settle();
	document.querySelector('.catalog-card-pick-layer').click();
	await settle();
	document.querySelector('.catalog-placement-popover button').click();
	await settle();
	assert.ok(document.querySelector('.planner-panel .course-code'));
	const toggle = [...document.querySelectorAll('label')]
		.find((label) => label.textContent.includes('HoS'))
		.querySelector('input');
	toggle.click();
	await settle();
	input(document.querySelector('textarea'), '02451,10060');
	const volume = [...document.querySelectorAll('label')]
		.find((label) => label.textContent.includes('Course volume'))
		.querySelector('input');
	for (const year of ['2026', '2025', '2024', '2026']) {
		input(volume, year);
		document
			.querySelector('form')
			.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true }));
		await settle();
		const button = document.querySelector('button[type="submit"]');
		assert.equal(button.disabled, false, `Load button stuck in volume ${year}`);
		assert.equal(button.textContent.trim(), 'Load courses');
		assert.equal(document.querySelectorAll('.catalog .card').length, year === '2024' ? 1 : 2);
		assert.equal(document.querySelectorAll('.placement-option').length, 4);
	}
	console.log(
		'PASS missing 02451 and duplicate Physics schemes do not freeze loading across 2026 → 2025 → 2024 → 2026'
	);

	const searchInput = document.querySelector('#course-search');
	const searchButton = document.querySelector('.search-input-row button');
	const dialog = document.querySelector('.course-basket');
	const importedCount = () => document.querySelectorAll('.catalog .card').length;
	const queryCount = () => requests.filter((url) => url.startsWith('/api/courses/search?')).length;
	input(searchInput, 'mathematics');
	await settle();
	assert.equal(queryCount(), 0, 'Typing must not start search');
	searchInput.dispatchEvent(
		new window.KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true })
	);
	await settle();
	assert.equal(queryCount(), 1);
	assert.equal(dialog.open, true);
	assert.equal(document.body.style.overflow, 'hidden');
	assert.equal(dialog.querySelectorAll('.card').length, 3);
	// Dismissing a selection must not commit its schedule or checkbox state.
	dialog.querySelectorAll('input[type="checkbox"]')[1].click();
	[...dialog.querySelectorAll('.placement-option')]
		.find((button) => button.textContent.trim() === 'B')
		.click();
	await settle();
	dialog.querySelector('.basket-close').click();
	await settle();
	assert.equal(importedCount(), 2);
	searchButton.click();
	await settle();
	assert.equal(dialog.querySelectorAll('input[type="checkbox"]')[1].checked, false);
	assert.ok(dialog.querySelectorAll('.card')[1].textContent.includes('E1A'));
	const checks = dialog.querySelectorAll('input[type="checkbox"]');
	assert.equal(checks[0].disabled, true, 'Imported courses cannot be selected again');
	checks[1].click();
	checks[2].click();
	const schemeB = [...dialog.querySelectorAll('.placement-option')].find(
		(button) => button.textContent.trim() === 'B'
	);
	schemeB.click();
	await settle();
	assert.equal(importedCount(), 2, 'Selection must not import immediately');
	assert.ok(dialog.querySelector('.basket-footer').textContent.includes('2 selected'));
	assert.ok(dialog.querySelectorAll('.card')[1].textContent.includes('E3A'));
	dialog.querySelector('.basket-footer button').click();
	await settle();
	assert.equal(dialog.open, false);
	assert.equal(document.activeElement, searchInput);
	assert.equal(document.body.style.overflow, '');
	assert.equal(importedCount(), 4);
	assert.ok(
		document.querySelector('.planner-panel .course-code'),
		'Existing placements must survive import'
	);
	assert.ok(document.querySelector('textarea').value.includes('01001'));
	assert.ok(document.querySelector('textarea').value.includes('01002'));
	const importedMath = [...document.querySelectorAll('.catalog .card')].find((card) =>
		card.textContent.includes('01001')
	);
	assert.ok(
		importedMath.textContent.includes('E3A'),
		'Basket placement choice must transfer on import'
	);
	document
		.querySelector('form')
		.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true }));
	await settle();
	assert.equal(importedCount(), 4, 'Reload must retain search imports from cache');
	searchButton.click();
	await settle();
	assert.ok(
		[...dialog.querySelectorAll('input[type="checkbox"]')].every((input) => input.disabled)
	);
	assert.ok(dialog.querySelector('.basket-footer button').disabled);
	dialog.dispatchEvent(new window.Event('cancel', { cancelable: true }));
	await settle();
	assert.equal(dialog.open, false, 'Escape/cancel closes basket');
	searchMode = 'empty';
	searchButton.click();
	await settle();
	assert.ok(dialog.textContent.includes('No courses found'));
	dialog.querySelector('.basket-close').click();
	await settle();
	searchMode = 'error';
	searchButton.click();
	await settle();
	assert.ok(dialog.querySelector('[role="alert"]').textContent.includes('DTU unavailable'));
	searchMode = 'success';
	[...dialog.querySelectorAll('button')]
		.find((button) => button.textContent.includes('Retry'))
		.click();
	await settle();
	assert.equal(dialog.querySelectorAll('.card').length, 3);
	dialog.getBoundingClientRect = () => ({ left: 100, right: 900, top: 100, bottom: 600 });
	dialog.dispatchEvent(
		new window.MouseEvent('click', { clientX: 200, clientY: 200, bubbles: true })
	);
	await settle();
	assert.equal(dialog.open, true, 'Clicking dialog frame must not dismiss');
	dialog.dispatchEvent(new window.MouseEvent('click', { clientX: 50, clientY: 50, bubbles: true }));
	await settle();
	assert.equal(dialog.open, false, 'Clicking backdrop must dismiss');
	searchMode = 'delayed';
	searchButton.click();
	await settle();
	assert.ok(dialog.textContent.includes('Searching courses'));
	dialog.querySelector('.basket-close').click();
	await settle();
	releaseSearch();
	await settle();
	assert.equal(dialog.open, false, 'Late response must not reopen dismissed basket');
	assert.equal(dialog.querySelectorAll('.card').length, 0, 'Dismissed response must be ignored');
	searchButton.click();
	await settle();
	input(volume, '2025');
	await settle();
	assert.equal(dialog.open, false, 'Catalogue change closes stale basket');
	releaseSearch();
	await settle();
	assert.equal(dialog.querySelectorAll('.card').length, 0);
	input(volume, '2026');
	searchMode = 'delayed';
	searchButton.click();
	await settle();
	dialog.querySelector('.basket-close').click();
	await settle();
	searchMode = 'empty';
	input(searchInput, 'new query');
	searchButton.click();
	await settle();
	releaseSearch();
	await settle();
	assert.equal(dialog.open, true);
	assert.ok(
		dialog.textContent.includes('No courses found'),
		'Older search must not replace newer results'
	);
	dialog.querySelector('.basket-close').click();
	await settle();
	input(searchInput, '   ');
	await settle();
	assert.equal(searchButton.disabled, true);
	console.log(
		'PASS search submission, multiple selection, additive imports, placement choices, duplicate prevention, reload, dismissal, errors and stale results'
	);
} finally {
	await unmount(component);
	await window.happyDOM.close();
}
