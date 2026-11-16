// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The host-imports lint (#322): no file outside src/webview imports from it, and src/shared, which both
// builds compile, has no DOM to lean on. A planted import is named at its line; a planted DOM reference
// fails the shared type check.

const assert = require('node:assert/strict');
const {describe, it} = require('node:test');
const fs = require('fs');
const os = require('os');
const path = require('path');
const {spawnSync} = require('child_process');

const {offences} = require('./lint-host-imports');

const CLIENT = path.join(__dirname, '..');
const SRC = path.join(CLIENT, 'src');
const SHARED_TSCONFIG = path.join(SRC, 'shared', 'tsconfig.json');
const TSC = path.join(CLIENT, 'node_modules', 'typescript', 'bin', 'tsc');

describe('an import of webview code', () => {
    it('is reported in a host file, at its line', () => {
        const root = path.join(os.tmpdir(), 'src');
        const text = 'import * as vscode from "vscode";\nimport { x } from "./webview/preview/x";\n';
        assert.deepEqual(offences(text, path.join(root, 'host.ts'), root).map(o => o.line), [2]);
    });

    it('is reported in a shared module', () => {
        const root = path.join(os.tmpdir(), 'src');
        const text = 'import type { Y } from "../webview/y";\n';
        assert.deepEqual(offences(text, path.join(root, 'shared', 'z.ts'), root).map(o => o.line), [1]);
    });

    it('is reported as an export-from, a dynamic import and a require', () => {
        const root = path.join(os.tmpdir(), 'src');
        const text = 'export { a } from "./webview/a";\nconst b = import("./webview/b");\nconst c = require("./webview/c");\n';
        assert.deepEqual(offences(text, path.join(root, 'host.ts'), root).map(o => o.line), [1, 2, 3]);
    });

    it('is not reported for shared or protocol code', () => {
        const root = path.join(os.tmpdir(), 'src');
        const text = 'import { a } from "./shared/a";\nimport { b } from "./protocol/b";\n';
        assert.deepEqual(offences(text, path.join(root, 'host.ts'), root), []);
    });
});

describe('the lint run', () => {
    it('fails on a planted import and names the file and line', () => {
        const root = fs.mkdtempSync(path.join(os.tmpdir(), 'host-imports-'));
        try {
            fs.mkdirSync(path.join(root, 'webview'));
            fs.writeFileSync(path.join(root, 'webview', 'view.ts'), 'export const v = 1;\n');
            fs.writeFileSync(path.join(root, 'host.ts'), '\nimport { v } from "./webview/view";\n');

            const run = spawnSync(process.execPath, [path.join(__dirname, 'lint-host-imports.js'), root],
                {encoding: 'utf8'});

            assert.equal(run.status, 1, run.stderr);
            assert.match(run.stderr, /host\.ts:2 {2}imports \.\/webview\/view/);
        } finally {
            fs.rmSync(root, {recursive: true, force: true});
        }
    });

    it('passes on the shipped tree', () => {
        const run = spawnSync(process.execPath, [path.join(__dirname, 'lint-host-imports.js')], {encoding: 'utf8'});
        assert.equal(run.status, 0, run.stderr);
    });
});

describe('the shared modules', () => {
    it('are type-checked without the DOM', () => {
        // Read the way tsc reads it: a tsconfig may carry comments, which JSON.parse refuses.
        const ts = require(path.join(CLIENT, 'node_modules', 'typescript'));
        const {config: tsconfig, error} = ts.readConfigFile(SHARED_TSCONFIG, ts.sys.readFile);
        assert.equal(error, undefined);
        const libs = (tsconfig.compilerOptions.lib ?? []).map(l => l.toLowerCase());
        assert.ok(libs.length > 0, 'the shared tsconfig names its libs');
        assert.deepEqual(libs.filter(l => l.startsWith('dom')), []);
    });

    it('fail the type check on a planted DOM reference', () => {
        const root = fs.mkdtempSync(path.join(os.tmpdir(), 'shared-dom-'));
        try {
            fs.copyFileSync(SHARED_TSCONFIG, path.join(root, 'tsconfig.json'));
            fs.writeFileSync(path.join(root, 'planted.ts'), 'export const title = document.title;\n');

            const run = spawnSync(process.execPath, [TSC, '-p', root, '--noEmit'], {encoding: 'utf8'});

            assert.notEqual(run.status, 0, 'a DOM reference type-checked under the shared tsconfig');
            assert.match(run.stdout, /planted\.ts.*Cannot find name 'document'/);
        } finally {
            fs.rmSync(root, {recursive: true, force: true});
        }
    });
});
