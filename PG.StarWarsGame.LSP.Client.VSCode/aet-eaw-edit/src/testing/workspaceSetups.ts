// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

/**
 * The windows the client's project tests run against (#280): one project, and two projects side by
 * side - a mod over the base project it references, as the E2E suite's two-project window lays them
 * out. A test that can depend on how many projects the window runs runs against both.
 *
 * Pure data and a discovery over it; nothing here imports `vscode`.
 */

import { discoverProjects, type Discovery } from "../projectDiscovery";

export interface SetupProject {
    /** The project's name, as its project file states it. */
    name: string;
    /** The project file. */
    path: string;
}

export interface WorkspaceSetup {
    name: string;
    /** The workspace folders, as VS Code reports them. */
    folders: string[];
    /** The projects, the one the author works on (the mod) first. */
    projects: SetupProject[];
    /** Every project file's text, by path. */
    files: Record<string, string>;
}

function pgproj(name: string, references: string[] = []): string {
    return JSON.stringify({
        name,
        directories: { xml: ["Data/Xml"], scripts: ["Data/Scripts"] },
        projectReferences: references.map((path) => ({ path })),
    });
}

export const ONE_PROJECT: WorkspaceSetup = {
    name: "one project",
    folders: ["c:/Work/Mod"],
    projects: [{ name: "Mod", path: "c:/Work/Mod/Mod.pgproj" }],
    files: { "c:/Work/Mod/Mod.pgproj": pgproj("Mod") },
};

export const TWO_PROJECTS: WorkspaceSetup = {
    name: "two projects",
    folders: ["c:/Work/Window"],
    projects: [
        { name: "Mod", path: "c:/Work/Window/Mod/Mod.pgproj" },
        { name: "Base", path: "c:/Work/Window/Base/Base.pgproj" },
    ],
    files: {
        "c:/Work/Window/Mod/Mod.pgproj": pgproj("Mod", ["../Base/Base.pgproj"]),
        "c:/Work/Window/Base/Base.pgproj": pgproj("Base"),
    },
};

export const WORKSPACE_SETUPS: readonly WorkspaceSetup[] = [
    ONE_PROJECT,
    TWO_PROJECTS,
];

/** The setup's projects as the extension discovers them. */
export function discoverSetup(setup: WorkspaceSetup): Discovery {
    const byKey = new Map(
        Object.entries(setup.files).map(([p, t]) => [p.toLowerCase(), t]),
    );
    return discoverProjects({
        folders: setup.folders,
        projectFiles: Object.keys(setup.files),
        read: (path) => byKey.get(path.toLowerCase()),
    });
}

/** A file under a project's own folder, as VS Code spells the path. */
export function fileOf(project: SetupProject, relative: string): string {
    return `${project.path.slice(0, project.path.lastIndexOf("/"))}/${relative}`;
}
