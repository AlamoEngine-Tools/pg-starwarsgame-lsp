// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The campaign navigator and its commands: the story graph, a story's files and a set's rename.

import * as vscode from "vscode";

import { StoryGraphPanel } from "../storyGraphPanel";
import { type StoryGraphTarget, graphTargets } from "../storyGraphTarget";
import { setNameProblem } from "../storyNavigatorModel";
import {
    StoryNavigatorViewProvider,
    StoryTreeItem,
} from "../storyNavigatorViewProvider";

import type { HostDeps } from "./host";

export function register(
    context: vscode.ExtensionContext,
    deps: HostDeps,
): void {
    const {
        commandProjectKey,
        gatewayFor,
        panelProject,
        storyNavigatorProvider,
    } = deps;

    context.subscriptions.push(
        vscode.window.registerTreeDataProvider(
            StoryNavigatorViewProvider.viewId,
            storyNavigatorProvider,
        ),
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.refreshStoryNavigator",
            () => storyNavigatorProvider?.refresh(),
        ),
        // Opens the read-only story graph panel. Invoked with the campaign tree item (inline icon
        // in the navigator) or without arguments from the command palette (quick-picks a campaign).
        // A graph is one campaign FACTION, so the tree offers this on its faction node and hands
        // both halves over. The palette has no node to hand over, so it asks - once, over a flat
        // list of every pair, rather than a campaign prompt followed by a faction prompt.
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.openStoryGraph",
            async (arg?: StoryTreeItem) => {
                // A clicked row belongs to the project whose tree it is in.
                const projectKey =
                    (arg && storyNavigatorProvider?.projectOf(arg)) ??
                    (await commandProjectKey());
                if (projectKey === null) {
                    return;
                }
                const lsp = gatewayFor(projectKey);
                if (!lsp.requireRunning()) {
                    return;
                }

                // A battle node carries its key in fileName and its name in the label; it opens the
                // battle's own graph, the sub-graph the galactic one shows as a portal.
                let target: StoryGraphTarget | undefined =
                    arg?.campaignName && arg?.factionName
                        ? {
                              campaign: arg.campaignName,
                              faction: arg.factionName,
                              ...(arg.kind === "battle" && arg.fileName
                                  ? {
                                        scope: arg.fileName,
                                        scopeLabel: String(
                                            arg.label ?? arg.fileName,
                                        ),
                                    }
                                  : {}),
                          }
                        : undefined;

                if (!target) {
                    const result = await lsp.requestOrReport(
                        "aet/getStoryPlots",
                        {},
                        vscode.l10n.t("Cannot load story campaigns"),
                    );
                    if (!result) {
                        return;
                    }
                    if (result.error) {
                        vscode.window.showWarningMessage(
                            vscode.l10n.t("EaWEdit: {0}", result.error),
                        );
                        return;
                    }

                    const items = graphTargets(result.campaigns ?? []);
                    if (!items.length) {
                        vscode.window.showInformationMessage(
                            vscode.l10n.t(
                                "EaWEdit: No story campaigns found in this workspace.",
                            ),
                        );
                        return;
                    }
                    const picked = await vscode.window.showQuickPick(items, {
                        title: vscode.l10n.t("Open Story Graph"),
                        placeHolder: vscode.l10n.t(
                            "Select a campaign faction or one of its battles",
                        ),
                    });
                    target = picked?.target;
                }
                if (target) {
                    StoryGraphPanel.show(
                        target,
                        context.extensionUri,
                        panelProject(projectKey),
                        undefined,
                        false,
                    );
                }
            },
        ),
        // The server's resolution is the only one: the model resolved every manifest entry to its
        // document when it was assembled, case-insensitively as the engine reads files. There is
        // no name search here - a workspace glob is case-sensitive and would answer by a different
        // rule than the one the model used, which is how a file that was right there read as missing.
        // Invoked from a navigator node's inline button with the node; a node with nothing to open
        // offers the disabled twin instead, so this never runs for one.
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.openStoryFile",
            async (item?: StoryTreeItem) => {
                if (!item?.openUri) {
                    return;
                }
                try {
                    const doc = await vscode.workspace.openTextDocument(
                        vscode.Uri.parse(item.openUri),
                    );
                    const line = item.openLine ?? 0;
                    await vscode.window.showTextDocument(doc, {
                        preview: true,
                        selection: new vscode.Range(line, 0, line, 0),
                    });
                } catch (e) {
                    vscode.window.showWarningMessage(
                        vscode.l10n.t(
                            "EaWEdit: Cannot open '{0}' - {1}",
                            String(item.label),
                            String(e),
                        ),
                    );
                }
            },
        ),
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.openStoryFileUnavailable",
            () => undefined,
        ),
        // A Campaign_Set has no declaration of its own: it is the value every member writes. The
        // server's rename rewrites that value in every member the project owns, so this asks it
        // at one of them - the position the plots feed gave the node - and applies the edit as
        // the refactoring it is, which saves the files it touched when the editor is set to.
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.renameCampaignSet",
            async (item?: StoryTreeItem) => {
                const at = item?.renameAt;
                const current = item?.setName;
                if (!at || !current) {
                    return;
                }
                const entered = await vscode.window.showInputBox({
                    title: vscode.l10n.t("Rename Set"),
                    prompt: vscode.l10n.t(
                        "New name for every member in the project",
                    ),
                    value: current,
                    validateInput: (value) => setNameProblem(value, current),
                });
                if (entered === undefined) {
                    return;
                }
                const refuse = (reason: string): void => {
                    vscode.window.showWarningMessage(
                        vscode.l10n.t(
                            "EaWEdit: Cannot rename the set '{0}' - {1}",
                            current,
                            reason,
                        ),
                    );
                };
                try {
                    const uri = vscode.Uri.parse(at.uri);
                    const document =
                        await vscode.workspace.openTextDocument(uri);
                    // The position is the feed's, and the file may have moved on since: a rename
                    // asked one line off would rename whatever stands there now.
                    const written = document.getText(
                        new vscode.Range(
                            at.line,
                            at.column,
                            at.line,
                            at.column + current.length,
                        ),
                    );
                    if (written.toLowerCase() !== current.toLowerCase()) {
                        refuse(
                            vscode.l10n.t(
                                "the campaign file changed since the list was read",
                            ),
                        );
                        return;
                    }
                    const edit = await vscode.commands.executeCommand<
                        vscode.WorkspaceEdit | undefined
                    >(
                        "vscode.executeDocumentRenameProvider",
                        uri,
                        new vscode.Position(at.line, at.column),
                        entered.trim(),
                    );
                    if (!edit || edit.size === 0) {
                        refuse(vscode.l10n.t("the server offered no rename"));
                        return;
                    }
                    if (
                        !(await vscode.workspace.applyEdit(edit, {
                            isRefactoring: true,
                        }))
                    ) {
                        refuse(vscode.l10n.t("the edit was not applied"));
                    }
                } catch (e) {
                    refuse(String(e));
                }
            },
        ),
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.renameCampaignSetNoMember",
            () => undefined,
        ),
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.renameCampaignSetUngrouped",
            () => undefined,
        ),
    );
}
