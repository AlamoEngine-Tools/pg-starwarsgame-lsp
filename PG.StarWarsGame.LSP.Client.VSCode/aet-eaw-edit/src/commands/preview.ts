// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The model preview's commands: a model, an assembled unit, the shaders and the preview cache.

import * as vscode from "vscode";

import { LspGateway } from "../lsp/lspGateway";
import {
    type ModelPickItem,
    modelPickItems,
    pickedModelName,
    previewSubjectFor,
} from "../modelPicker";
import { ModelPreviewPanel } from "../modelPreviewPanel";
import { offerShaderSources } from "../shaderSources";

import type { HostDeps } from "./host";

/**
 * Asks which model to preview, from every model the project can reach - its own, its dependencies'
 * and the base game's - so a model that is not in the explorer can be found without its name.
 *
 * A QuickPick that also accepts what was typed: the list is the asset catalog, and a model saved a
 * moment ago may not be in it yet.
 */
async function pickModel(gateway: LspGateway): Promise<string | null> {
    const result = await gateway.requestOrReport(
        "aet/listModels",
        {},
        vscode.l10n.t("Could not list models"),
    );
    if (!result) {
        return null;
    }

    const pick = vscode.window.createQuickPick<ModelPickItem>();
    pick.title = vscode.l10n.t("Preview Model");
    pick.placeholder = vscode.l10n.t(
        "Model file name, e.g. EV_StarDestroyer.ALO",
    );
    pick.matchOnDescription = true;
    pick.items = modelPickItems(result.models);

    const name = await new Promise<string | null>((resolve) => {
        pick.onDidAccept(() => {
            resolve(pickedModelName(pick.activeItems[0], pick.value));
            pick.hide();
        });
        pick.onDidHide(() => resolve(null));
        pick.show();
    });

    pick.dispose();
    return name;
}

export function register(
    context: vscode.ExtensionContext,
    deps: HostDeps,
): void {
    const { commandProjectKey, gatewayFor, panelProject } = deps;

    context.subscriptions.push(
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.previewModel",
            async (referenceArg?: string, projectKeyArg?: string) => {
                const projectKey = projectKeyArg ?? (await commandProjectKey());
                if (projectKey === null) {
                    return;
                }
                const lsp = gatewayFor(projectKey);
                if (!lsp.requireRunning()) {
                    return;
                }

                const name = referenceArg?.trim() || (await pickModel(lsp));
                if (!name) {
                    return;
                }

                ModelPreviewPanel.show(
                    context.extensionUri,
                    panelProject(projectKey),
                    previewSubjectFor(name),
                    name,
                );
            },
        ),
    );

    // Never wired to activation: a modder who has not asked for shader-accurate previews should not
    // be nagged about a download they may not want.
    context.subscriptions.push(
        vscode.commands.registerCommand(
            "aet-eaw-edit.shaders.obtain",
            async () => {
                await offerShaderSources(context);
            },
        ),
    );

    context.subscriptions.push(
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.clearPreviewCache",
            async () => {
                const projectKey = await commandProjectKey();
                if (projectKey === null) {
                    return;
                }
                // The cache is the server's: it knows the project's folder and stops a build that
                // would fill it again.
                const result = await gatewayFor(projectKey).requestOrReport(
                    "aet/clearPreviewCache",
                    {},
                    vscode.l10n.t("Could not clear the preview cache"),
                );
                if (result) {
                    void vscode.window.showInformationMessage(
                        vscode.l10n.t(
                            "Preview cache cleared - {0} converted models removed",
                            result.removed,
                        ),
                    );
                }
            },
        ),
    );

    context.subscriptions.push(
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.previewAssembledUnit",
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
                        title: vscode.l10n.t("Preview Assembled Unit"),
                        prompt: vscode.l10n.t(
                            "Name of the GameObject to assemble, e.g. Generic_Star_Destroyer",
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
                ModelPreviewPanel.show(
                    context.extensionUri,
                    panelProject(projectKey),
                    { kind: "object", objectId: id },
                    id,
                );
            },
        ),
    );
}
