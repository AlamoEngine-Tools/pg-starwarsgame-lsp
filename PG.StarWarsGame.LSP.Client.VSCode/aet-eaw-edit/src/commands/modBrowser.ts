// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The Mod Browser's outline view and the command that opens a page of it.

import * as vscode from "vscode";

import { ModBrowserPanel } from "../modBrowserPanel";
import {
    ModBrowserOutlineItem,
    ModBrowserViewProvider,
    OPEN_MOD_BROWSER,
} from "../modBrowserViewProvider";

import type { HostDeps } from "./host";

export function register(
    context: vscode.ExtensionContext,
    deps: HostDeps,
): void {
    const { commandProjectKey, modBrowserProvider, panelProject } = deps;

    context.subscriptions.push(
        vscode.window.registerTreeDataProvider(
            ModBrowserViewProvider.viewId,
            modBrowserProvider,
        ),
        // From an outline row, or from the palette, which opens the game data.
        vscode.commands.registerCommand(
            OPEN_MOD_BROWSER,
            async (arg?: ModBrowserOutlineItem) => {
                const projectKey =
                    (arg && modBrowserProvider?.projectOf(arg)) ??
                    (await commandProjectKey());
                if (projectKey === null) {
                    return;
                }
                const project = panelProject(projectKey);
                if (!project.gateway.requireRunning()) {
                    return;
                }
                ModBrowserPanel.show(
                    context.extensionUri,
                    project,
                    arg?.outline ?? "gameData",
                );
            },
        ),
    );
}
