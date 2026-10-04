// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import {describe, it} from 'node:test';

import {type BugReportClientInfo, flattenFlags, formatBugReport} from './bugReport';
import {type ServerStatus} from './protocol/serverStatus';

const client: BugReportClientInfo = {
    extensionVersion: '0.4.0',
    vscodeVersion: '1.105.0',
    os: 'win32 x64',
    workspaceFolders: 2,
    workspaceFile: true,
    featureFlags: {'tools.modelPreview': true, 'story.editor': false},
};

const server: ServerStatus = {
    serverVersion: '0.4.0+c80d25e',
    runtime: '.NET 10.0.1',
    schema: {version: '2.3.0', compatibility: 'Supported', source: 'Official'},
    baseline: {source: 'Cache', official: true, builtAt: '2026-09-16T10:00:00Z', manifestHash: 'ab12'},
    iconPack: {source: 'Network', official: true},
    workspace: {projectDetected: true, projectValid: true, projectProblem: null, foldersWithoutProject: 1},
    index: {state: 'Complete'},
    warnings: ['Lua symbols: 0'],
};

const extendedServer: ServerStatus = {
    ...server,
    extended: {
        dependencies: {direct: 1, total: 2, depth: 2, unresolved: 0, layers: 3},
        indexDurationMs: 3250,
        files: {projectFiles: 40, dependencyFiles: 12, xmlFiles: 45, luaFiles: 7},
        symbols: {xml: 900, lua: 30, baseline: 12000, localisationKeys: 400},
        assets: [
            {extension: '.ala', total: 0, baseGame: 0},
            {extension: '.alo', total: 1500, baseGame: 1379},
        ],
        caches: {
            schema: 'Hit', snapshotLayers: 2, rebuiltLayers: 1, filesReused: 50, filesParsed: 2,
            boneLayersReused: 3, boneLayers: 3,
        },
        symbolTypes: [{typeName: 'SpaceUnit', count: 20}, {typeName: 'LuaGlobal', count: 4}],
        memory: {
            heapBytes: 320 * 1048576,
            committedBytes: 400 * 1048576,
            fragmentedBytes: 12 * 1048576,
            largeObjectHeapBytes: 90 * 1048576,
            largeObjectHeapFragmentedBytes: 30 * 1048576,
            workingSetBytes: 441 * 1048576,
            gen2Collections: 7,
            layers: [
                {rank: 0, documents: 883, symbols: 8922, references: 30000},
                {rank: 1, documents: 999, symbols: 32289, references: 90000},
            ],
            caches: [
                {name: 'xml-parse', entries: 16, approximateBytes: null, hits: 120, misses: 40, evictions: 3},
                {
                    name: 'lua-parser-state',
                    entries: 1432,
                    approximateBytes: 2100000,
                    hits: null,
                    misses: null,
                    evictions: null
                },
                {name: 'xml-fixes', entries: 580, approximateBytes: null, hits: null, misses: null, evictions: null},
            ],
        },
    },
};

describe('formatBugReport, extended tier', () => {
    it('leaves the extended section out of a basic report', () => {
        assert.ok(!formatBugReport(client, extendedServer).includes('Extended stats'));
    });

    it('reports dependencies, index, files, symbols, assets and caches as counts', () => {
        const text = formatBugReport(client, extendedServer, {extended: true});

        for (const line of [
            '- Dependencies: 1 direct, 2 total, depth 2, 0 unresolved, 3 layers',
            '- Index duration: 3.3 s',
            '- Files: 40 project, 12 dependency (45 XML, 7 Lua)',
            '- Symbols: 900 XML, 30 Lua, 12000 baseline, 400 localisation keys',
            '- Assets: .ala 0, .alo 1500 (1379 base game)',
            '- Caches: schema Hit; index snapshots 2 of 3 layers, 50 files reused, 2 parsed; '
            + 'bone catalog 3 of 3 layers',
        ]) {
            assert.ok(text.includes(line), `missing "${line}" in:\n${text}`);
        }
    });

    it('reports memory, the index per layer rank and every cache, all as counts', () => {
        const text = formatBugReport(client, extendedServer, {extended: true});

        for (const line of [
            '- Memory: heap 320 MB, committed 400 MB, fragmented 12 MB; large object heap 90 MB '
            + '(30 MB fragmented); working set 441 MB; 7 gen2 collections',
            '- Layers: rank 0 883 documents, 8922 symbols, 30000 references; '
            + 'rank 1 999 documents, 32289 symbols, 90000 references',
            '- Cache entries: xml-parse 16 (120 hits, 40 misses, 3 evictions); '
            + 'lua-parser-state 1432 (2.0 MB); xml-fixes 580',
        ]) {
            assert.ok(text.includes(line), `missing "${line}" in:\n${text}`);
        }
    });

    it('lists symbol types only when asked to', () => {
        // Dozens of lines: fine on the clipboard, too long for a prefilled issue.
        assert.ok(!formatBugReport(client, extendedServer, {extended: true}).includes('SpaceUnit'));

        const text = formatBugReport(client, extendedServer, {extended: true, symbolTypes: true});
        assert.ok(text.includes('  - SpaceUnit: 20'));
        assert.ok(text.includes('  - LuaGlobal: 4'));
    });
});

describe('flattenFlags', () => {
    it('names each flag by its dotted path', () => {
        assert.deepEqual(
            flattenFlags({xml: {hover: true, rename: false}, tools: {modelPreview: true}}),
            {'xml.hover': true, 'xml.rename': false, 'tools.modelPreview': true});
    });
});

describe('formatBugReport', () => {
    it('reports versions, sources, workspace flags and index state', () => {
        const text = formatBugReport(client, server);

        for (const line of [
            '- Extension: 0.4.0',
            '- VS Code: 1.105.0',
            '- OS: win32 x64',
            '- Server: 0.4.0+c80d25e (.NET 10.0.1)',
            '- Schema: 2.3.0 - Supported, Official',
            '- Baseline: Cache, official, built 2026-09-16T10:00:00Z, manifest ab12',
            '- Icon pack: Network, official',
            '- Workspace: Multi-root (2 folders), workspace file',
            '- Project: Detected, valid',
            '- Folders without a project: 1',
            '- Index: Complete',
            '- Features off: story.editor',
        ]) {
            assert.ok(text.includes(line), `missing "${line}" in:\n${text}`);
        }
    });

    it('puts warnings at the top, before anything else is read', () => {
        const text = formatBugReport(client, server);

        assert.ok(text.indexOf('Lua symbols: 0') < text.indexOf('- Extension:'));
    });

    it('names the project problem when there is one', () => {
        const text = formatBugReport(client, {
            ...server,
            workspace: {...server.workspace, projectValid: false, projectProblem: 'Unparseable'},
        });

        assert.ok(text.includes('- Project: Detected, invalid - Unparseable'));
    });

    it('still reports the client when the server is not running', () => {
        const text = formatBugReport(client, null);

        assert.ok(text.includes('- Extension: 0.4.0'));
        assert.ok(text.includes('- Server: Not running'));
    });

    it('carries no path and no project name', () => {
        // The block goes into PUBLIC issues. Flags and counts only, by agreement.
        const text = formatBugReport(client, server);

        assert.doesNotMatch(text, /[A-Za-z]:[\\/]|\/home\/|\/Users\//);
    });

    it('is ASCII only', () => {
        assert.doesNotMatch(formatBugReport(client, server), /[^\x00-\x7F]/);
    });
});
