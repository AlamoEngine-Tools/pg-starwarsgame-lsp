// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// `aet/getServerStatus` - what the server can say about itself for a bug report. Flags, versions
// and counts only: the result is pasted into PUBLIC issues, so it never carries a path or a name.

/** Where the schema came from. */
export interface ServerStatusSchema {
    /** The schema's declared version, or null when it declares none. */
    version: string | null;
    /** Supported, Unversioned, OutOfRange, Unsupported, Malformed or NotChecked. */
    compatibility: string;
    /** Release, CachedRelease, Branch, CustomUrl, Local or NotLoaded. */
    source: string;
}

/** Where the baseline came from, as actually used. */
export interface ServerStatusBaseline {
    /** Network, Cache, Local, Empty or NotLoaded. */
    source: string;
    /** True when it came from the default URL. */
    official: boolean;
    builtAt: string | null;
    manifestHash: string | null;
}

/** Where the icon pack came from, as actually used. */
export interface ServerStatusIconPack {
    /** Network, Cache, Local, Empty or NotLoaded - it loads on the first preview, not at startup. */
    source: string;
    official: boolean;
}

/** The server's checks on the workspace, as outcomes. */
export interface ServerStatusWorkspace {
    projectDetected: boolean;
    projectValid: boolean;
    /** Why the project is not valid, from a fixed list - or null. */
    projectProblem: string | null;
    /** Workspace folders the server found no project in. */
    foldersWithoutProject: number;
}

/** The opt-in tier: counts that help rebuild a setup as a test case. */
export interface ServerStatusExtended {
    /** `layers` counts project layers, the root included. */
    dependencies: { direct: number; total: number; depth: number; unresolved: number; layers: number };
    indexDurationMs: number | null;
    /** Indexed documents: the root project's own layer, every other layer, and by language. */
    files: { projectFiles: number; dependencyFiles: number; xmlFiles: number; luaFiles: number };
    /** Story dialog files are not in the game index, so there is no dialog count. */
    symbols: { xml: number; lua: number; baseline: number; localisationKeys: number };
    /** Every extension the asset catalog records, the empty ones included. */
    assets: { extension: string; total: number; baseGame: number }[];
    /** `schema` is Hit, Rebuilt or NotUsed. */
    caches: {
        schema: string;
        snapshotLayers: number;
        rebuiltLayers: number;
        filesReused: number;
        filesParsed: number;
        boneLayersReused: number;
        boneLayers: number;
    };
    /** Project symbols by type, most first. */
    symbolTypes: { typeName: string; count: number }[];
    /**
     * The process's memory and where it goes. GC figures are read without forcing a collection;
     * layers are by rank, never by name; caches are every in-memory cache the server registers,
     * with nulls where a cache does not count that.
     */
    memory: {
        heapBytes: number;
        committedBytes: number;
        fragmentedBytes: number;
        largeObjectHeapBytes: number;
        largeObjectHeapFragmentedBytes: number;
        workingSetBytes: number;
        gen2Collections: number;
        layers: { rank: number; documents: number; symbols: number; references: number }[];
        caches: {
            name: string;
            entries: number;
            approximateBytes: number | null;
            hits: number | null;
            misses: number | null;
            evictions: number | null;
        }[];
    };
}

/** Result of `aet/getServerStatus`, basic tier. */
export interface ServerStatus {
    /** The full informational version, commit included. */
    serverVersion: string;
    runtime: string;
    schema: ServerStatusSchema;
    baseline: ServerStatusBaseline;
    iconPack: ServerStatusIconPack;
    workspace: ServerStatusWorkspace;
    /** Building, Complete or Failed. */
    index: { state: string };
    /** Anything empty or zero that should not be, one line each. */
    warnings: string[];
    /** Present only when the request asked for `extended`. */
    extended?: ServerStatusExtended | null;
}
