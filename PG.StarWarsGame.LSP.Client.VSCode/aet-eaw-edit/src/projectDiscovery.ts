// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

/**
 * Which projects a workspace holds, and which of them owns a given file (#228).
 *
 * One language server runs per open project, so the extension has to know the projects before it
 * starts anything. This module is the whole of that decision and has no `vscode` import: the
 * extension globs the folders for project files and hands the paths in, and gets back the set of
 * projects to start, each with the directories its server owns.
 *
 * The rules:
 * - An open project is a `.pgproj` under any workspace folder. Its identity is its path compared
 *   without regard to separator or case, so one file reached through two folders is one project.
 * - A project file inside another open project's directory is that project's dependency, not a
 *   project of its own: the outer project's directory already covers every file in it, and two
 *   servers publishing for one file would show every finding twice.
 * - A dependency under no folder - reached only through `projectReferences` - is assigned to the
 *   first open project that references it, in folder order, and to no other.
 * - A file under no project's directories belongs to no server.
 *
 * Paths come out forward-slashed. Comparisons fold case, as the server's own project resolution
 * does.
 */

// The ES module build: the package's default entry is a UMD wrapper whose dynamic requires esbuild
// cannot follow, so a bundle built from it fails at load with "Cannot find module './impl/format'".
import { parse, type ParseError } from "jsonc-parser/lib/esm/main";

/** What the extension found, and how to read a project file it names. */
export interface DiscoveryInput {
    /** The workspace folders, in the order the window lists them. */
    folders: readonly string[];
    /** Every `.pgproj` found under the folders, in any order and possibly more than once. */
    projectFiles: readonly string[];
    /** A project file's text, or undefined when it cannot be read. */
    read: (path: string) => string | undefined;
}

/** One project that gets a server of its own. */
export interface DiscoveredProject {
    /** The project file, forward-slashed. */
    path: string;
    /** The project's name, or the file's stem when the file names none or cannot be read. */
    name: string;
    /** The directory holding the project file. */
    directory: string;
    /** The directories the project file declares, resolved against {@link directory}. */
    directories: string[];
    /** The project files it references, transitively, dependencies first; itself excluded. */
    closure: string[];
    /** The dependencies under no folder that this project's server owns. */
    libraries: string[];
    /** The directories whose files this project's server owns. */
    roots: string[];
}

/** The workspace's projects, and who owns what. */
export interface Discovery {
    /** The projects to start, in folder order, then by path within a folder. */
    projects: DiscoveredProject[];
    /** Project files served as a dependency of the open project whose directory holds them. */
    nested: string[];
    /** Folders that hold no project file and lie inside no project. */
    foldersWithoutProject: string[];
    /** Project files that could not be read or parsed; their projects start, the server says why. */
    unreadable: string[];
    /** The project whose server owns a file, or undefined when none does. */
    ownerOf: (path: string) => DiscoveredProject | undefined;
}

interface ProjectFile {
    name?: string;
    directories: string[];
    references: string[];
    readable: boolean;
}

export function discoverProjects(input: DiscoveryInput): Discovery {
    const folders = input.folders.map(normalize);
    const files = new Map<string, ProjectFile | undefined>();
    const load = (path: string): ProjectFile | undefined => {
        const key = keyOf(path);
        if (!files.has(key)) {
            files.set(key, readProjectFile(input.read(path)));
        }
        return files.get(key);
    };

    // Folder order first, then path order within a folder: discovery order decides who owns a
    // shared dependency, so it has to be the same on every start.
    const candidates: string[] = [];
    const seen = new Set<string>();
    for (const folder of folders) {
        const inFolder = input.projectFiles
            .map(normalize)
            .filter((p) => isInside(p, folder))
            .sort((a, b) => keyOf(a).localeCompare(keyOf(b)));
        for (const path of inFolder) {
            if (!seen.has(keyOf(path))) {
                seen.add(keyOf(path));
                candidates.push(path);
            }
        }
    }

    const nested = candidates.filter((p) =>
        candidates.some(
            (outer) =>
                outer !== p &&
                isInside(dirname(p), dirname(outer)) &&
                keyOf(dirname(p)) !== keyOf(dirname(outer)),
        ),
    );
    const open = candidates.filter((p) => !nested.includes(p));

    const unreadable: string[] = [];
    const projects = open.map((path): DiscoveredProject => {
        const file = load(path);
        if (!file?.readable) {
            unreadable.push(path);
        }
        const directory = dirname(path);
        return {
            path,
            name: file?.name ?? stem(path),
            directory,
            directories:
                file?.directories.map((d) => resolve(directory, d)) ?? [],
            closure: closureOf(path, load),
            libraries: [],
            roots: [],
        };
    });

    const openKeys = new Set(open.map(keyOf));
    const underFolder = (path: string) =>
        folders.some((f) => isInside(path, f));
    const assigned = new Set<string>();
    // A declared directory inside another open project's directory stays that project's: two
    // servers must never own one file.
    const openDirectories = projects.map((p) => p.directory);
    for (const project of projects) {
        const others = openDirectories.filter((d) => d !== project.directory);
        project.roots = rootsOf(project.directory, project.directories, others);
        for (const dependency of project.closure) {
            const key = keyOf(dependency);
            if (
                openKeys.has(key) ||
                assigned.has(key) ||
                underFolder(dependency)
            ) {
                continue;
            }
            assigned.add(key);
            const file = load(dependency);
            const directory = dirname(dependency);
            project.libraries.push(dependency);
            project.roots.push(
                ...rootsOf(
                    directory,
                    file?.directories.map((d) => resolve(directory, d)) ?? [],
                    openDirectories,
                ),
            );
        }
    }

    const foldersWithoutProject = folders.filter(
        (folder) =>
            !candidates.some((p) => isInside(p, folder)) &&
            !projects.some((project) =>
                project.roots.some((root) => isInside(folder, root)),
            ),
    );

    const ownerOf = (path: string): DiscoveredProject | undefined => {
        const target = normalize(path);
        let owner: DiscoveredProject | undefined;
        let longest = -1;
        for (const project of projects) {
            for (const root of project.roots) {
                if (isInside(target, root) && root.length > longest) {
                    owner = project;
                    longest = root.length;
                }
            }
        }
        return owner;
    };

    return { projects, nested, foldersWithoutProject, unreadable, ownerOf };
}

/** The project files a project references, transitively, dependencies first; cycles broken. */
function closureOf(
    root: string,
    load: (path: string) => ProjectFile | undefined,
): string[] {
    const ordered: string[] = [];
    const emitted = new Set<string>();
    const onStack = new Set<string>();

    const visit = (path: string) => {
        const key = keyOf(path);
        if (emitted.has(key) || onStack.has(key)) {
            return;
        }
        const file = load(path);
        if (path !== root && !file?.readable) {
            return;
        }
        onStack.add(key);
        for (const reference of file?.references ?? []) {
            visit(resolve(dirname(path), reference));
        }
        onStack.delete(key);
        emitted.add(key);
        ordered.push(path);
    };

    visit(root);
    return ordered.filter((p) => keyOf(p) !== keyOf(root));
}

/** A project's directory, plus each declared directory that lies outside it. */
function rootsOf(
    directory: string,
    directories: string[],
    taken: readonly string[],
): string[] {
    const roots = [directory];
    for (const declared of directories) {
        if (
            !roots.some((r) => isInside(declared, r)) &&
            !taken.some((t) => isInside(declared, t))
        ) {
            roots.push(declared);
        }
    }
    return roots;
}

/** The parts of a project file discovery needs; `readable` false when it does not parse. */
function readProjectFile(text: string | undefined): ProjectFile {
    const unreadable: ProjectFile = {
        directories: [],
        references: [],
        readable: false,
    };
    if (text === undefined) {
        return unreadable;
    }

    const errors: ParseError[] = [];
    const json: unknown = parse(text, errors, {
        allowTrailingComma: true,
        disallowComments: false,
    });
    if (errors.length > 0 || typeof json !== "object" || json === null) {
        return unreadable;
    }

    const doc = json as {
        name?: unknown;
        directories?: Record<string, unknown>;
        localisation?: { directory?: unknown };
        projectReferences?: unknown;
    };
    const directories: string[] = [];
    for (const value of Object.values(doc.directories ?? {})) {
        if (Array.isArray(value)) {
            for (const entry of value) {
                if (typeof entry === "string" && !directories.includes(entry)) {
                    directories.push(entry);
                }
            }
        }
    }
    if (typeof doc.localisation?.directory === "string") {
        directories.push(doc.localisation.directory);
    }

    const references = Array.isArray(doc.projectReferences)
        ? doc.projectReferences
              .map((r: unknown) => (r as { path?: unknown })?.path)
              .filter((p): p is string => typeof p === "string")
        : [];

    return {
        name:
            typeof doc.name === "string" && doc.name.trim() !== ""
                ? doc.name
                : undefined,
        directories,
        references,
        readable: true,
    };
}

function normalize(path: string): string {
    const slashed = path.replace(/\\/g, "/");
    return slashed.length > 1 &&
        slashed.endsWith("/") &&
        !/^[A-Za-z]:\/$/.test(slashed)
        ? slashed.slice(0, -1)
        : slashed;
}

function keyOf(path: string): string {
    return normalize(path).toLowerCase();
}

function dirname(path: string): string {
    const index = path.lastIndexOf("/");
    return index < 0 ? "" : path.slice(0, index);
}

function stem(path: string): string {
    const file = path.slice(path.lastIndexOf("/") + 1);
    const dot = file.lastIndexOf(".");
    return dot > 0 ? file.slice(0, dot) : file;
}

/** Whether `path` is `directory` or lies under it, on a segment boundary. */
function isInside(path: string, directory: string): boolean {
    const p = keyOf(path);
    const d = keyOf(directory);
    return p === d || p.startsWith(d.endsWith("/") ? d : d + "/");
}

/**
 * A reference or a declared directory resolved against a project's directory. An absolute path is
 * kept; `.` and `..` fold, and a `..` at the top - the drive or the root - is dropped.
 */
function resolve(directory: string, candidate: string): string {
    const path = normalize(candidate);
    if (path.startsWith("/") || /^[A-Za-z]:/.test(path)) {
        return path;
    }

    const segments = directory.split("/").filter((s) => s !== "");
    const prefix = directory.startsWith("/") ? "/" : "";
    for (const segment of path.split("/")) {
        if (segment === "" || segment === ".") {
            continue;
        } else if (segment === "..") {
            if (segments.length > 1) {
                segments.pop();
            }
        } else {
            segments.push(segment);
        }
    }
    return prefix + segments.join("/");
}
