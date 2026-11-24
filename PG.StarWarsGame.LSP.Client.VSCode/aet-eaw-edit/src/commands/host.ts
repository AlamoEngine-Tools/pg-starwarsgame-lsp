// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What the extension host hands every command module (#324).
//
// A command lives in the module named after its area, and each module exports one
// `register(context, deps)`. Everything a command needs from the running host - which project, its
// gateway, its panels' project, the navigators - comes through here, so a module names what it
// depends on and never reaches into the host's own state.

import type * as vscode from "vscode";

import type { LocTreeItem } from "../localisationNavigatorViewProvider";
import type { LspGateway } from "../lsp/lspGateway";
import type { ModBrowserOutlineItem } from "../modBrowserViewProvider";
import type { PanelProject } from "../panelProject";
import type { ProjectClient } from "../projectClient";
import type { ProjectGroupedTree } from "../projectGroupedTree";
import type { Stores } from "../stores";
import type { StoryTreeItem } from "../storyNavigatorViewProvider";

export interface HostDeps {
    /** The running servers by project key, in discovery order. */
    readonly clients: ReadonlyMap<string, ProjectClient>;
    /**
     * The window's own gateway, which follows the document in front. For a request that belongs to
     * no project, or to whichever is in front; a panel never gets it.
     */
    readonly lsp: LspGateway;
    /** Everything the extension remembers between sessions. */
    readonly stores: Stores;
    readonly localisationNavigatorProvider: ProjectGroupedTree<LocTreeItem>;
    readonly storyNavigatorProvider: ProjectGroupedTree<StoryTreeItem>;
    readonly modBrowserProvider: ProjectGroupedTree<ModBrowserOutlineItem>;
    /** The read-only documents that show an object with its variants applied. */
    readonly effectiveObjectProvider: { refresh(uri: vscode.Uri): void };

    /** The gateway bound to one project for as long as the window runs it. */
    gatewayFor(key: string | undefined): LspGateway;
    /** The project a panel opened for `key` belongs to, for its whole life. */
    panelProject(key: string | undefined): PanelProject;
    /** The key given, or the single project's for the "" the navigators use. */
    resolveProjectKey(key: string | undefined): string;
    /**
     * The key of the project a command acts on, "" with none running, null when the picker was
     * dismissed.
     */
    commandProjectKey(): Promise<string | null>;
    /** The same for a localisation command: the asking panel's, else the clicked row's. */
    locCommandProjectKey(arg: unknown): Promise<string | null>;
    /** The project whose server owns a file, when one does. */
    ownerOf(fsPath: string): ProjectClient | undefined;
    /** The project a workspace folder stands for. */
    folderProject(folder: vscode.WorkspaceFolder): ProjectClient | undefined;
    /** Asks which project, and remembers the answer for this window. */
    pickProject(): Promise<ProjectClient | undefined>;

    /** Starts and stops servers until they match the projects the window holds. */
    reconcileClients(context: vscode.ExtensionContext): Promise<void>;
    stopAllClients(): Promise<void>;
    /** Stops every server and starts the window's projects again. */
    restartServer(): Promise<void>;
    /** Redraws the status bar from the servers' states. */
    updateStatus(): void;
}
