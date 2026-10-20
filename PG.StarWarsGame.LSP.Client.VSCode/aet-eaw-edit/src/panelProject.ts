// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The project a panel belongs to (#276).
//
// A panel talks to one project's server for its whole life. It gets that project when it opens and
// never follows the active editor afterwards: a panel that did would show one project's data and
// write through another's server the moment a file of the other project came to the front. Its tab
// is filed under the project, its title names the project once the window runs several, and it
// takes only that project's refreshes.
//
// Pure, so the rule is testable without a VS Code host.

import type { LspGateway } from "./lsp/lspGateway";

/** The project a panel is bound to. */
export interface PanelProject {
    /**
     * The project's key: its normalised project file path. Empty only while no project runs, when
     * there is no server to bind to.
     */
    readonly key: string;
    /** The project's display name. */
    readonly name: string;
    /** Requests to this project's server, and to no other. */
    readonly gateway: LspGateway;
}

/**
 * The key a panel is filed under: its project and its subject.
 *
 * Never the subject alone. Two projects may each have a campaign, a model or a text file of one
 * name - a shared dependency's file is the same file in both - and those are two panels on two
 * servers.
 */
export function projectSubjectKey(projectKey: string, subject: string): string {
    return `${projectKey}|${subject}`;
}

/**
 * A panel's title: its own, with the project's name once the window runs more than one project.
 *
 * Two projects' panels for one subject would otherwise carry the same title, and nothing on the tab
 * would say which server is behind it.
 */
export function projectTitle(
    title: string,
    projectName: string,
    projectCount: number,
): string {
    return projectCount > 1 && projectName
        ? `${title} - ${projectName}`
        : title;
}
