import { createRequire } from 'node:module';
import os from 'node:os';

const require = createRequire(import.meta.url);

const platformPackages = {
	darwin: {
		arm64: {
			rollup: ['@rollup/rollup-darwin-arm64'],
			esbuild: '@esbuild/darwin-arm64'
		},
		x64: {
			rollup: ['@rollup/rollup-darwin-x64'],
			esbuild: '@esbuild/darwin-x64'
		}
	},
	win32: {
		arm64: {
			rollup: ['@rollup/rollup-win32-arm64-msvc'],
			esbuild: '@esbuild/win32-arm64'
		},
		ia32: {
			rollup: ['@rollup/rollup-win32-ia32-msvc'],
			esbuild: '@esbuild/win32-ia32'
		},
		x64: {
			rollup: ['@rollup/rollup-win32-x64-msvc', '@rollup/rollup-win32-x64-gnu'],
			esbuild: '@esbuild/win32-x64'
		}
	}
};

const expected = platformPackages[os.platform()]?.[os.arch()];

if (!expected) {
	process.exit(0);
}

const hasModule = (name) => {
	try {
		require.resolve(`${name}/package.json`);
		return true;
	} catch {
		return false;
	}
};

const hasExpectedRollup = expected.rollup.some(hasModule);
const hasExpectedEsbuild = hasModule(expected.esbuild);

if (hasExpectedRollup && hasExpectedEsbuild) {
	process.exit(0);
}

const platformLabel = `${os.platform()} ${os.arch()}`;
const rollupLabel = expected.rollup.join(' or ');

console.error(
	[
		`This frontend install does not match the current machine (${platformLabel}).`,
		`Expected ${rollupLabel} and ${expected.esbuild} in node_modules.`,
		'The synced node_modules directory was likely installed on a different OS.',
		'Fix: delete frontend/node_modules, run npm install on this machine, and try again.'
	].join('\n')
);

process.exit(1);
