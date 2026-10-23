// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

/**
 * Which language clients the window runs, and what each one covers (#229).
 *
 * One server runs per open project. With one project - or none, a folder laid out like a mod without
 * a project file - the window runs a single client: every xml, lua and dialog file goes to it, it is
 * rooted at the first folder and logs there. A folder that is neither gets no client: any xml or lua
 * file activates the extension. Only with several projects does each client narrow to its own
 * directories, because two clients selecting one document would both publish for it and every
 * finding would show twice.
 *
 * Pure, so the decisions are tested here and the extension only carries them out.
 */

import type { Discovery } from "./projectDiscovery";

/** One client the window should be running. */
export interface ClientSpec {
    /** Stable identity across rediscoveries: the project file, or the window for the single client. */
    key: string;
    /** What the client is shown as. */
    name: string;
    /** The project file the server loads, or undefined to let the server look for one. */
    projectPath?: string;
    /** The root the server is given, for detection and relative paths. */
    workspaceRoot: string;
    /** Where the server writes its log. */
    logDirectory: string;
    /** The directories whose documents this client takes; undefined takes every document. */
    roots?: string[];
    /** The directories whose on-disk changes this client is told about; undefined is all of them. */
    watched?: string[];
    /** Changes whenever anything above changes, so a changed client is restarted. */
    signature: string;
}

/** The key of the single client a one-project window runs. */
export const WINDOW_CLIENT = "window";

/**
 * @param folders The workspace folders as VS Code reports them; the single client is handed the
 *     first one unchanged, as it always was.
 * @param canonical Spells a directory as it is on disk. A referenced project's directory is spelled
 *     as the project file wrote it, and a document selector is matched against VS Code's own
 *     spelling of the path.
 * @param isModFolder Whether a folder without a project file is laid out like a mod.
 */
export function clientSpecs(
    discovery: Discovery,
    folders: readonly string[],
    canonical: (directory: string) => string = (d) => d,
    isModFolder: (directory: string) => boolean = () => false,
): ClientSpec[] {
    if (discovery.projects.length <= 1) {
        const first = folders[0];
        const project = discovery.projects[0];
        if (
            first === undefined ||
            (project === undefined && !isModFolder(first))
        ) {
            return [];
        }
        return [
            withSignature({
                key: WINDOW_CLIENT,
                name: project?.name ?? "EaWEdit",
                projectPath: project?.path,
                workspaceRoot: first,
                logDirectory: first,
            }),
        ];
    }

    return discovery.projects.map((project) =>
        withSignature({
            key: project.path.toLowerCase(),
            name: project.name,
            projectPath: project.path,
            workspaceRoot: project.directory,
            logDirectory: project.directory,
            roots: project.roots.map(canonical),
            watched: unique(
                [...project.roots, ...project.closure.map(directoryOf)].map(
                    canonical,
                ),
            ),
        }),
    );
}

/** Which running clients to stop and which specs to start. A changed client is both. */
export function planChanges(
    running: ReadonlyMap<string, string>,
    specs: readonly ClientSpec[],
): { start: ClientSpec[]; stop: string[] } {
    const wanted = new Map(specs.map((s) => [s.key, s]));
    const stop = [...running.keys()].filter(
        (key) => wanted.get(key)?.signature !== running.get(key),
    );
    const start = specs.filter((s) => running.get(s.key) !== s.signature);
    return { start, stop };
}

/**
 * The name of a client's diagnostic collection: its own per project.
 *
 * vscode-languageclient names the collection after the client id unless told otherwise, and every
 * project's client shares that id. VS Code warned that the name already existed the moment a second
 * project published (measured in the extension host log), and only one of two same-named files
 * showed its diagnostics.
 */
export function diagnosticCollectionNameFor(
    clientId: string,
    spec: ClientSpec,
): string {
    return `${clientId}:${spec.key}`;
}

/** A document selector in the shape vscode-languageclient takes. */
export interface DocumentFilter {
    scheme: string;
    language: string;
    pattern?: string;
}

/**
 * The documents a client takes. Story-dialog scripts open as plaintext, so `.txt` is selected and
 * the server decides which of them are dialog files.
 */
export function documentSelectorFor(spec: ClientSpec): DocumentFilter[] {
    if (spec.roots === undefined) {
        return [
            { scheme: "file", language: "xml" },
            { scheme: "file", language: "lua" },
            { scheme: "file", language: "plaintext", pattern: "**/*.txt" },
        ];
    }

    // VS Code matches the pattern against its own spelling of the path, case-sensitively: a root
    // spelled "C:/..." selected nothing against its lower-case drive letter, and a file opened under
    // another spelling of its path reached no server (#291). A project owns a path the way the
    // server compares one, ignoring case - as ownerKey does.
    return spec.roots.map(anyCase).flatMap((root) => [
        { scheme: "file", language: "xml", pattern: `${root}/**` },
        { scheme: "file", language: "lua", pattern: `${root}/**` },
        { scheme: "file", language: "plaintext", pattern: `${root}/**/*.txt` },
    ]);
}

/** A glob for `path` in any case: each letter a class of both of its cases. */
function anyCase(path: string): string {
    return slashed(path).replace(
        /[A-Za-z]/g,
        (c) => `[${c.toLowerCase()}${c.toUpperCase()}]`,
    );
}

/**
 * The client whose server owns `path` (#230): the one whose roots hold it, the deepest root winning;
 * the single client owns every file. Undefined when the file lies in no project.
 */
export function ownerKey(
    specs: readonly ClientSpec[],
    path: string,
): string | undefined {
    if (specs.length === 1 && specs[0].roots === undefined) {
        return specs[0].key;
    }
    const target = slashed(path).toLowerCase();
    let owner: string | undefined;
    let longest = -1;
    for (const spec of specs) {
        for (const root of spec.roots ?? []) {
            const r = slashed(root).toLowerCase();
            if (
                (target === r || target.startsWith(r + "/")) &&
                r.length > longest
            ) {
                owner = spec.key;
                longest = r.length;
            }
        }
    }
    return owner;
}

/** Whether a client is told about an on-disk change to `path`. */
export function ownsPath(spec: ClientSpec, path: string): boolean {
    return (
        spec.watched === undefined ||
        spec.watched.some((directory) => isInside(path, directory))
    );
}

/** Whether `path` is `directory` or lies under it, on a segment boundary, ignoring separator and case. */
export function isInside(path: string, directory: string): boolean {
    const target = slashed(path).toLowerCase();
    const d = slashed(directory).toLowerCase();
    return target === d || target.startsWith(d + "/");
}

/** Where one client stands. */
export type ClientState =
    "starting" | "indexing" | "ready" | "failed" | "stopped";

/**
 * The status bar text. One client reads exactly as the single client always has; several count the
 * projects indexed and name any that failed or stopped.
 */
export function statusText(
    clients: readonly { name: string; state: ClientState }[],
): string {
    if (clients.length === 1) {
        switch (clients[0].state) {
            case "starting":
                return "$(loading~spin) EaWEdit LSP: Starting...";
            case "indexing":
                return "$(loading~spin) EaWEdit LSP: Indexing workspace...";
            case "failed":
                return "$(error) EaWEdit LSP: Failed to start";
            case "stopped":
                return "$(circle-slash) EaWEdit LSP: Stopped";
            default:
                return "$(check) EaWEdit LSP";
        }
    }

    const count = (state: ClientState) =>
        clients.filter((c) => c.state === state).length;
    const ready = count("ready");
    const failed = count("failed");
    const stopped = count("stopped");
    const busy = count("starting") + count("indexing");

    const problems = [
        failed > 0 ? `${failed} failed` : undefined,
        stopped > 0 ? `${stopped} stopped` : undefined,
    ].filter((p) => p !== undefined);
    const icon =
        failed > 0 ? "$(error)" : busy > 0 ? "$(loading~spin)" : "$(check)";
    const indexed =
        ready === clients.length
            ? `${ready} projects indexed`
            : `${ready} of ${clients.length} projects indexed`;
    return [`${icon} EaWEdit LSP: ${indexed}`, ...problems].join(", ");
}

function withSignature(spec: Omit<ClientSpec, "signature">): ClientSpec {
    return {
        ...spec,
        signature: JSON.stringify([
            spec.projectPath,
            spec.workspaceRoot,
            spec.logDirectory,
            spec.roots,
            spec.watched,
        ]),
    };
}

function slashed(path: string): string {
    return path.replace(/\\/g, "/");
}

function directoryOf(path: string): string {
    const index = path.lastIndexOf("/");
    return index < 0 ? path : path.slice(0, index);
}

function unique(paths: string[]): string[] {
    const seen = new Set<string>();
    return paths.filter((p) => {
        const key = p.toLowerCase();
        if (seen.has(key)) {
            return false;
        }
        seen.add(key);
        return true;
    });
}
