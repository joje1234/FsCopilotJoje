import assert from 'assert/strict';
import { mkdtempSync, readFileSync, writeFileSync, existsSync, statSync } from 'fs';
import { tmpdir } from 'os';
import { join } from 'path';
import { spawnSync } from 'child_process';
import { fileURLToPath } from 'url';

const folder = mkdtempSync(join(tmpdir(), 'fsc-controls-package-test-'));
const script = fileURLToPath(new URL('../scripts/package.mjs', import.meta.url));
const output = join(folder, 'package');
const run = (...args) => spawnSync(process.execPath, [script, ...args, '--output', output], { encoding: 'utf8' });
let result = run('--wasm', join(folder, 'missing.wasm'));
assert.notEqual(result.status, 0);
assert.equal(existsSync(output), false);
const wasm = join(folder, 'test-only.wasm');
writeFileSync(wasm, Buffer.from([0, 97, 115, 109, 1, 0, 0, 0]));
result = run('--wasm', wasm);
assert.notEqual(result.status, 0);
assert.match(result.stderr, /does not export module_init/);
assert.equal(existsSync(output), false);

// Synthetic module for packaging tests only, written to a temporary directory.
// It has the callback exports but implements no simulator functionality.
const names = ['module_init', 'module_deinit', 'Update_StandAlone'];
const exports = [3, ...names.flatMap((name, index) => [name.length, ...Buffer.from(name), 0, index])];
writeFileSync(wasm, Buffer.from([
    0, 97, 115, 109, 1, 0, 0, 0,
    1, 4, 1, 96, 0, 0,
    3, 4, 3, 0, 0, 0,
    7, exports.length, ...exports,
    10, 10, 3, 2, 0, 11, 2, 0, 11, 2, 0, 11
]));
result = run('--wasm', wasm);
assert.equal(result.status, 0, result.stderr);
const layout = JSON.parse(readFileSync(join(output, 'layout.json'), 'utf8'));
const manifest = JSON.parse(readFileSync(join(output, 'manifest.json'), 'utf8'));
assert.equal(manifest.package_version, '0.9.12');
const paths = layout.content.map(item => item.path);
for (const path of ['html_ui/efb_ui/efb_apps/FsCopilotControls/FsCopilotControls.js',
    'html_ui/efb_ui/efb_apps/FsCopilotControls/Assets/icon.svg', 'modules/FsCopilotControls.wasm', 'THIRD_PARTY_NOTICES.txt', 'LICENSE.txt', 'manifest.json']) {
    assert(paths.includes(path), 'Missing package payload: ' + path);
}
for (const item of layout.content) {
    assert.equal(item.size, statSync(join(output, item.path)).size);
    assert(item.date > 116444736000000000);
}
assert(!paths.includes('layout.json'));
console.log('Package tests passed: invalid binaries rejected; EFB paths, WASM exports, licenses and layout sizes verified in a temporary directory.');
