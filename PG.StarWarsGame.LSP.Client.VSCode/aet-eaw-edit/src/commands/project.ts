// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The commands about the window's projects and their servers: which project, restart, reload, revalidate, a new project.

import * as l10n from "@vscode/l10n";
import * as fs from "fs";
import * as vscode from "vscode";

import { resolveServerCommand } from "../serverCommand";
import { serverProblem } from "../serverLaunch";

import type { HostDeps } from "./host";

export function register(
    context: vscode.ExtensionContext,
    deps: HostDeps,
): void {
    const {
        clients,
        commandProjectKey,
        gatewayFor,
        lsp,
        pickProject,
        reconcileClients,
        restartServer,
        stopAllClients,
        updateStatus,
    } = deps;

    // Changes the project remembered for commands; the window asks on its own only when the
    // document in front belongs to no project and nothing was picked yet.
    context.subscriptions.push(
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.selectProject",
            async () => {
                if (clients.size <= 1) {
                    void vscode.window.showInformationMessage(
                        vscode.l10n.t("EaWEdit: One project open"),
                    );
                    return;
                }
                await pickProject();
            },
        ),
    );

    context.subscriptions.push(
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.debug.forceStartup",
            async () => {
                await stopAllClients();
                const serverMissing = serverProblem(
                    resolveServerCommand(context.extensionPath, []),
                    fs.existsSync,
                );
                if (serverMissing) {
                    void vscode.window.showErrorMessage(serverMissing);
                } else {
                    await reconcileClients(context);
                }
            },
        ),
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.restartServer",
            restartServer,
        ),
    );

    context.subscriptions.push(
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.revalidateWorkspace",
            async () => {
                // The workspace is the window: every project's server revalidates its own files.
                // Asking one project left the others' diagnostics as they were (#167).
                const keys = clients.size > 0 ? [...clients.keys()] : [""];
                await Promise.all(
                    keys.map((key) =>
                        gatewayFor(key).executeCommand(
                            "aet-eaw-edit.lsp.revalidateWorkspace",
                            [],
                            l10n.t("could not revalidate the workspace"),
                        ),
                    ),
                );
            },
        ),
    );

    context.subscriptions.push(
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.newModProject",
            async () => {
                // Writes a new project wherever it is told to; no open project is asked about it.
                if (!lsp.requireRunning()) {
                    return;
                }
                const name = await vscode.window.showInputBox({
                    prompt: vscode.l10n.t("Mod name"),
                    placeHolder: vscode.l10n.t("My Awesome Mod"),
                    validateInput: (v) =>
                        v?.trim() ? null : vscode.l10n.t("Name is required"),
                });
                if (!name) {
                    return;
                }
                const folders = await vscode.window.showOpenDialog({
                    canSelectFolders: true,
                    canSelectFiles: false,
                    canSelectMany: false,
                    openLabel: vscode.l10n.t("Select mod root folder"),
                });
                if (!folders?.length) {
                    return;
                }
                // The server says what it did - created, or refused because a project file exists. The
                // client announced "created" whenever the command returned, refusals included.
                await lsp.executeCommand(
                    "aet-eaw-edit.lsp.newModProject",
                    [{ name: name.trim(), path: folders[0].fsPath }],
                    vscode.l10n.t("Mod project not created - {0}", name.trim()),
                );
            },
        ),
    );

    context.subscriptions.push(
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.reloadProject",
            async () => {
                const projectKey = await commandProjectKey();
                if (projectKey === null) {
                    return;
                }
                // The reload indexes the project again; the status says so until the server's
                // $/workspaceScanComplete, which ends a reload as it ends startup.
                const project = clients.get(projectKey);
                if (
                    project?.state === "ready" ||
                    project?.state === "loading"
                ) {
                    project.state = "indexing";
                    project.phase = undefined;
                    updateStatus();
                }
                await gatewayFor(projectKey).executeCommand(
                    "aet-eaw-edit.lsp.reloadProject",
                    [],
                    l10n.t("could not reload the project"),
                );
            },
        ),
    );
}
