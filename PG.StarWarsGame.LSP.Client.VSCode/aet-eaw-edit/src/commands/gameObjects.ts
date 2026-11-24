// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The commands that show one game object: its encyclopedia card and its effective definition.

import * as vscode from "vscode";

import {
    effectiveObjectPath,
    effectiveObjectQuery,
} from "../effectiveObjectUri";
import { EFFECTIVE_SCHEME } from "../effectiveObjectUri";
import { EncyclopediaPanel } from "../encyclopediaPanel";
import { GET_ENCYCLOPEDIA_ENTRY } from "../protocol";
import { encyclopediaScreenParams } from "../stores";

import type { HostDeps } from "./host";

export function register(
    context: vscode.ExtensionContext,
    deps: HostDeps,
): void {
    const {
        clients,
        commandProjectKey,
        effectiveObjectProvider,
        gatewayFor,
        panelProject,
        resolveProjectKey,
        stores,
    } = deps;

    // Opens the encyclopedia popup preview for a GameObject. Triggered by the "preview encyclopedia"
    // code lens (objectId passed as the first argument), or from the command palette (prompts).

    context.subscriptions.push(
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.showEncyclopedia",
            async (objectIdArg?: string, projectKeyArg?: string) => {
                const projectKey = projectKeyArg ?? (await commandProjectKey());
                if (projectKey === null) {
                    return;
                }
                const lsp = gatewayFor(projectKey);
                if (!lsp.requireRunning()) {
                    return;
                }

                let objectId = objectIdArg;
                if (!objectId) {
                    objectId = await vscode.window.showInputBox({
                        title: vscode.l10n.t("Preview Encyclopedia Popup"),
                        prompt: vscode.l10n.t(
                            "Name of the GameObject whose popup to preview",
                        ),
                        validateInput: (v) =>
                            v?.trim()
                                ? null
                                : vscode.l10n.t("Object name is required"),
                    });
                }
                if (!objectId?.trim()) {
                    return;
                }
                const id = objectId.trim();

                // Both the first open and the SP/MP toggle go through here, so the panel never has to
                // know how to reach the server - it asks, this fetches.
                const load = async (
                    multiplayer: boolean,
                    retarget: boolean,
                ): Promise<void> => {
                    const entry = await lsp.requestOrReport(
                        GET_ENCYCLOPEDIA_ENTRY,
                        {
                            objectId: id,
                            multiplayer,
                            ...encyclopediaScreenParams(stores),
                        },
                        vscode.l10n.t("Could not load the encyclopedia entry"),
                    );
                    if (entry === undefined) {
                        return;
                    }

                    if (retarget) {
                        EncyclopediaPanel.show(
                            context.extensionUri,
                            panelProject(projectKey),
                            entry,
                            (mp) => void load(mp, false),
                        );
                    } else {
                        EncyclopediaPanel.update(
                            resolveProjectKey(projectKey),
                            entry,
                        );
                    }
                };

                // Retargeting keeps whatever SP/MP mode the panel is already showing.
                await load(
                    EncyclopediaPanel.multiplayer(
                        resolveProjectKey(projectKey),
                    ),
                    true,
                );
            },
        ),
    );

    // Opens the read-only "effective object" virtual document for a variant GameObject. Triggered by
    // the "show effective object" code lens (objectId passed as the first argument), or from the command
    // palette (prompts for the object name).
    context.subscriptions.push(
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.showEffectiveObject",
            async (
                objectIdArg?: string,
                projectKeyArg?: string | null,
                typeNameArg?: string | null,
            ) => {
                const projectKey = projectKeyArg ?? (await commandProjectKey());
                if (projectKey === null) {
                    return;
                }
                const lsp = gatewayFor(projectKey);
                if (!lsp.requireRunning()) {
                    return;
                }

                let objectId = objectIdArg;
                if (!objectId) {
                    objectId = await vscode.window.showInputBox({
                        title: vscode.l10n.t("Show Effective Object"),
                        prompt: vscode.l10n.t(
                            "Name of the variant GameObject to resolve",
                        ),
                        validateInput: (v) =>
                            v?.trim()
                                ? null
                                : vscode.l10n.t("Object name is required"),
                    });
                }
                if (!objectId?.trim()) {
                    return;
                }
                objectId = objectId.trim();

                // The tab label is derived from the URI's path basename, so bake the intent into it: this is a
                // generated, read-only preview of the fully-merged object, not an editable source file. The real
                // object id and its project travel in the query, so the decorative basename doesn't affect
                // content resolution.
                const project = panelProject(projectKey);
                const uri = vscode.Uri.from({
                    scheme: EFFECTIVE_SCHEME,
                    path: effectiveObjectPath(
                        objectId,
                        project.name,
                        clients.size,
                    ),
                    query: effectiveObjectQuery({
                        objectId,
                        projectKey: project.key,
                        typeName: typeNameArg ?? "",
                    }),
                });
                effectiveObjectProvider?.refresh(uri);
                const doc = await vscode.workspace.openTextDocument(uri);
                // Open beside the active editor so the preview sits next to the source it was generated from.
                await vscode.window.showTextDocument(doc, {
                    preview: true,
                    viewColumn: vscode.ViewColumn.Beside,
                });
            },
        ),
    );
}
