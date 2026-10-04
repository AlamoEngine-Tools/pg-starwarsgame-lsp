// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The bug report block: what the client knows about itself plus what the server reports, as one
// Markdown section. Kept free of `vscode` so the unit harness can run it. The block is pasted into
// PUBLIC issues, so it carries versions, flags and counts - never a path or a project name.

import {type ServerStatus, type ServerStatusExtended} from './protocol/serverStatus';

/** What only the client knows. */
export interface BugReportClientInfo {
    extensionVersion: string;
    vscodeVersion: string;
    /** Platform and architecture, e.g. `win32 x64`. */
    os: string;
    workspaceFolders: number;
    /** True when a `.code-workspace` file is open. */
    workspaceFile: boolean;
    /** Every feature flag by its dotted name. */
    featureFlags: Record<string, boolean>;
}

/** The extension's nested feature flags as `{ 'xml.hover': true, ... }`. */
export function flattenFlags(flags: object, prefix = ''): Record<string, boolean> {
    const flat: Record<string, boolean> = {};
    for (const [key, value] of Object.entries(flags)) {
        const name = prefix === '' ? key : `${prefix}.${key}`;
        if (typeof value === 'boolean') {
            flat[name] = value;
        } else if (value !== null && typeof value === 'object') {
            Object.assign(flat, flattenFlags(value as object, name));
        }
    }
    return flat;
}

/** Which parts of the report to include beyond the basic block. */
export interface BugReportOptions {
    /** The opt-in counts. Needs a server status fetched with `extended`. */
    extended?: boolean;
    /** The per-type symbol list: dozens of lines, so never in a prefilled issue. */
    symbolTypes?: boolean;
}

/** The report block. `server` is null when the server is not running. */
export function formatBugReport(
    client: BugReportClientInfo, server: ServerStatus | null, options: BugReportOptions = {},
): string {
    const lines: string[] = ['### EaWEdit bug report info', ''];

    // First, so the reader sees "everything is empty" before reading anything else.
    if (server !== null && server.warnings.length > 0) {
        lines.push('**Warnings**', '');
        lines.push(...server.warnings.map(w => `- ${w}`), '');
    }

    lines.push(`- Extension: ${client.extensionVersion}`);
    lines.push(`- VS Code: ${client.vscodeVersion}`);
    lines.push(`- OS: ${client.os}`);
    lines.push(`- Workspace: ${workspaceShape(client)}`);

    if (server === null) {
        lines.push('- Server: Not running');
    } else {
        const schema = server.schema;
        lines.push(`- Server: ${server.serverVersion} (${server.runtime})`);
        lines.push(`- Schema: ${schema.version ?? 'unversioned'} - ${schema.compatibility}, ${schema.source}`);

        const baseline = server.baseline;
        lines.push(`- Baseline: ${baseline.source}, ${baseline.official ? 'official' : 'custom'}`
            + (baseline.builtAt === null ? '' : `, built ${baseline.builtAt}`)
            + (baseline.manifestHash === null ? '' : `, manifest ${baseline.manifestHash}`));
        lines.push(`- Icon pack: ${server.iconPack.source}, ${server.iconPack.official ? 'official' : 'custom'}`);

        const workspace = server.workspace;
        lines.push(`- Project: ${workspace.projectDetected ? 'Detected' : 'Not detected'}`
            + (workspace.projectDetected
                ? workspace.projectValid ? ', valid' : `, invalid - ${workspace.projectProblem ?? 'Unknown'}`
                : ''));
        lines.push(`- Folders without a project: ${workspace.foldersWithoutProject}`);
        lines.push(`- Index: ${server.index.state}`);
    }

    const off = Object.entries(client.featureFlags).filter(([, on]) => !on).map(([name]) => name);
    lines.push(`- Features off: ${off.length === 0 ? 'none' : off.sort().join(', ')}`);

    const extended = server?.extended;
    if (options.extended === true && extended) {
        lines.push('', '**Extended stats**', '', formatExtendedStats(extended, options.symbolTypes === true));
    }

    return lines.join('\n') + '\n';
}

/**
 * The extended stats as their own block - the issue form gives them a field of their own, which is
 * also what lets a long prefill drop them first.
 */
export function formatExtendedStats(extended: ServerStatusExtended, symbolTypes: boolean): string {
    const lines: string[] = [];

    const deps = extended.dependencies;
    lines.push(`- Dependencies: ${deps.direct} direct, ${deps.total} total, depth ${deps.depth}, `
        + `${deps.unresolved} unresolved, ${deps.layers} layers`);
    if (extended.indexDurationMs !== null) {
        lines.push(`- Index duration: ${(extended.indexDurationMs / 1000).toFixed(1)} s`);
    }

    const files = extended.files;
    lines.push(`- Files: ${files.projectFiles} project, ${files.dependencyFiles} dependency `
        + `(${files.xmlFiles} XML, ${files.luaFiles} Lua)`);

    const symbols = extended.symbols;
    lines.push(`- Symbols: ${symbols.xml} XML, ${symbols.lua} Lua, ${symbols.baseline} baseline, `
        + `${symbols.localisationKeys} localisation keys`);

    lines.push(`- Assets: ${extended.assets
        .map(a => `${a.extension} ${a.total}` + (a.baseGame > 0 ? ` (${a.baseGame} base game)` : ''))
        .join(', ')}`);

    const caches = extended.caches;
    lines.push(`- Caches: schema ${caches.schema}; `
        + `index snapshots ${caches.snapshotLayers} of ${caches.snapshotLayers + caches.rebuiltLayers} layers, `
        + `${caches.filesReused} files reused, ${caches.filesParsed} parsed; `
        + `bone catalog ${caches.boneLayersReused} of ${caches.boneLayers} layers`);

    const memory = extended.memory;
    lines.push(`- Memory: heap ${mb(memory.heapBytes)} MB, committed ${mb(memory.committedBytes)} MB, `
        + `fragmented ${mb(memory.fragmentedBytes)} MB; `
        + `large object heap ${mb(memory.largeObjectHeapBytes)} MB `
        + `(${mb(memory.largeObjectHeapFragmentedBytes)} MB fragmented); `
        + `working set ${mb(memory.workingSetBytes)} MB; ${memory.gen2Collections} gen2 collections`);
    lines.push(`- Layers: ${memory.layers
        .map(l => `rank ${l.rank} ${l.documents} documents, ${l.symbols} symbols, ${l.references} references`)
        .join('; ')}`);
    lines.push(`- Cache entries: ${memory.caches.map(cacheEntry).join('; ')}`);

    if (symbolTypes) {
        lines.push('- Symbol types:');
        lines.push(...extended.symbolTypes.map(t => `  - ${t.typeName}: ${t.count}`));
    }

    return lines.join('\n');
}

function mb(bytes: number): number {
    return Math.round(bytes / 1048576);
}

/** `name entries (detail)`, the detail being whatever the cache counts: bytes, or hit/miss/eviction totals. */
function cacheEntry(cache: ServerStatusExtended['memory']['caches'][number]): string {
    const details: string[] = [];
    if (cache.approximateBytes !== null) {
        details.push(`${(cache.approximateBytes / 1048576).toFixed(1)} MB`);
    }
    if (cache.hits !== null || cache.misses !== null || cache.evictions !== null) {
        details.push(`${cache.hits ?? 0} hits, ${cache.misses ?? 0} misses, ${cache.evictions ?? 0} evictions`);
    }
    return details.length === 0 ? `${cache.name} ${cache.entries}` : `${cache.name} ${cache.entries} (${details.join(', ')})`;
}

function workspaceShape(client: BugReportClientInfo): string {
    const shape = client.workspaceFolders === 0
        ? 'No folder'
        : client.workspaceFolders === 1 ? 'Single folder' : `Multi-root (${client.workspaceFolders} folders)`;
    return client.workspaceFile ? `${shape}, workspace file` : shape;
}
