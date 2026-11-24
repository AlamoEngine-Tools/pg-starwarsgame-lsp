// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The localisation navigator and its commands: opening, creating, converting and exporting text files.

import * as l10n from "@vscode/l10n";
import * as path from "path";
import * as vscode from "vscode";

import { cfg } from "../hostSettings";
import { LocalisationEditorPanel } from "../localisationEditorPanel";
import {
    LocTreeItem,
    LocalisationNavigatorViewProvider,
} from "../localisationNavigatorViewProvider";
import { formatChoices, formatOutcome } from "../localisationProjectFormat";
import { LOC_CATEGORY } from "../protocol";

import type { HostDeps } from "./host";

export function register(
    context: vscode.ExtensionContext,
    deps: HostDeps,
): void {
    const {
        commandProjectKey,
        gatewayFor,
        locCommandProjectKey,
        localisationNavigatorProvider,
        panelProject,
    } = deps;

    context.subscriptions.push(
        vscode.window.registerTreeDataProvider(
            LocalisationNavigatorViewProvider.viewId,
            localisationNavigatorProvider,
        ),
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.refreshLocalisationNavigator",
            () => localisationNavigatorProvider?.refresh(),
        ),
        // Hosted here rather than in the tree so the palette can reach it too. Lifted out of the
        // sidebar webview, which owned this quick pick before the tree existed.
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.newLocalisationProject",
            async () => {
                const choice = await vscode.window.showQuickPick(
                    [
                        {
                            label: vscode.l10n.t(
                                "Initialise from the game baseline",
                            ),
                            command: "aet-eaw-edit.lsp.initLocalisationProject",
                        },
                        {
                            label: vscode.l10n.t(
                                "Import existing localisation files",
                            ),
                            command:
                                "aet-eaw-edit.lsp.importLocalisationProject",
                        },
                    ],
                    {
                        placeHolder: vscode.l10n.t(
                            "Create a localisation project",
                        ),
                    },
                );
                if (choice) {
                    await vscode.commands.executeCommand(choice.command);
                }
            },
        ),
        // Opens the localisation grid in its own tab. Invoked with the file tree item, or without
        // arguments from the command palette (quick-picks a file, grouped by category).
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.openLocalisationEditor",
            async (arg?: LocTreeItem) => {
                // A clicked row belongs to the project whose tree it is in.
                const projectKey =
                    (arg && localisationNavigatorProvider?.projectOf(arg)) ??
                    (await commandProjectKey());
                if (projectKey === null) {
                    return;
                }
                const lsp = gatewayFor(projectKey);
                if (!lsp.requireRunning()) {
                    return;
                }

                // The quick pick below is for the palette, where nothing was clicked. A node that
                // WAS clicked and carries no project is a node that should not have offered this
                // action - answering it by listing every localisation file in the workspace is how
                // clicking a credits group ended up offering unrelated .dat translation files.
                if (arg !== undefined && arg.project === undefined) {
                    vscode.window.showWarningMessage(
                        vscode.l10n.t(
                            "EaWEdit: That node groups several files rather than being one. Open one of the files under it.",
                        ),
                    );
                    return;
                }

                let project = arg?.project;
                if (!project) {
                    const result = await lsp.requestOrReport(
                        "aet/getLocalisationProjects",
                        {},
                        vscode.l10n.t("Could not list localisation files"),
                    );
                    if (!result) {
                        return;
                    }
                    if (result.error || !result.projects?.length) {
                        vscode.window.showWarningMessage(
                            vscode.l10n.t(
                                "EaWEdit: {0}",
                                result.error ??
                                    vscode.l10n.t(
                                        "no localisation files found in this workspace.",
                                    ),
                            ),
                        );
                        return;
                    }

                    const picked = await vscode.window.showQuickPick(
                        result.projects
                            .slice()
                            .sort(
                                (a, b) =>
                                    a.category.localeCompare(b.category) ||
                                    a.label.localeCompare(b.label),
                            )
                            .map((p) => ({
                                label: p.label,
                                description:
                                    p.category === LOC_CATEGORY.credits
                                        ? vscode.l10n.t("Credits")
                                        : vscode.l10n.t("Text"),
                                detail: p.filePath,
                                project: p,
                            })),
                        {
                            placeHolder: vscode.l10n.t(
                                "Open a localisation file",
                            ),
                        },
                    );
                    if (!picked) {
                        return;
                    }
                    project = picked.project;
                }

                // A node standing for a set of single-language files opens them as one table, so a
                // translation sits beside its source. A language inside that set opens the same
                // table with the other columns hidden - the set is the project either way, which is
                // why both are named after the set rather than after whichever file was clicked.
                LocalisationEditorPanel.showSet(
                    arg?.setFilePaths ?? [project.filePath],
                    arg?.setLabel ?? project.label,
                    project.category,
                    context.extensionUri,
                    panelProject(projectKey),
                    arg?.focusLanguage,
                );
            },
        ),
        // The crawl lives in the editor title bar, where a preview belongs - the same place a
        // Markdown or LaTeX editor puts one. Both are gated to the credits panel by its view type.
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.previewCreditsCrawl",
            () => LocalisationEditorPanel.previewCrawl(false),
        ),
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.previewCreditsCrawlFullScreen",
            () => LocalisationEditorPanel.previewCrawl(true),
        ),
        // Export was only ever reachable from inside the old sidebar webview; the tree is the
        // natural home for it, and a menu entry needs a real command behind it.
        // Opening the raw file is a thing you do to a file, so it belongs on the file's own menu in
        // the tree rather than inside the editor showing it.
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.openLocalisationAsText",
            async (arg?: LocTreeItem) => {
                const filePath = arg?.project?.filePath;
                if (!filePath) {
                    vscode.window.showWarningMessage(
                        vscode.l10n.t(
                            "EaWEdit: No localisation file selected.",
                        ),
                    );
                    return;
                }
                await vscode.window.showTextDocument(
                    vscode.Uri.file(filePath),
                    { viewColumn: vscode.ViewColumn.Beside },
                );
            },
        ),
        // Convert reaches here from two places - the editor's dock and the palette - so the request
        // and the way its outcome is reported live in one place rather than two that drift.
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.convertLocalisationFormat",
            async (
                arg?:
                    | LocTreeItem
                    | {
                          filePath?: string;
                          targetFormat?: string;
                          category?: string;
                          /** The asking panel's project (#276). */
                          projectKey?: string;
                      },
            ) => {
                const projectKey = await locCommandProjectKey(arg);
                if (projectKey === null) {
                    return;
                }
                const lsp = gatewayFor(projectKey);
                if (!lsp.requireRunning()) {
                    return;
                }

                const direct = arg as
                    | {
                          filePath?: string;
                          targetFormat?: string;
                          category?: string;
                      }
                    | undefined;
                let filePath =
                    direct?.filePath ??
                    (arg as LocTreeItem | undefined)?.project?.filePath;
                let category =
                    direct?.category ??
                    (arg as LocTreeItem | undefined)?.project?.category;

                if (!filePath) {
                    const result = await lsp.requestOrReport(
                        "aet/getLocalisationProjects",
                        {},
                        vscode.l10n.t("Could not list localisation files"),
                    );
                    if (!result) {
                        return;
                    }
                    if (result.error || !result.projects?.length) {
                        vscode.window.showWarningMessage(
                            vscode.l10n.t(
                                "EaWEdit: {0}",
                                result.error ??
                                    vscode.l10n.t(
                                        "no localisation files found in this workspace.",
                                    ),
                            ),
                        );
                        return;
                    }
                    const picked = await vscode.window.showQuickPick(
                        result.projects.map((p) => ({
                            label: p.label,
                            description: p.resourceType.toUpperCase(),
                            detail: p.filePath,
                            project: p,
                        })),
                        {
                            placeHolder: vscode.l10n.t(
                                "Convert which localisation file?",
                            ),
                        },
                    );
                    if (!picked) {
                        return;
                    }
                    filePath = picked.project.filePath;
                    category = picked.project.category;
                }

                let targetFormat = direct?.targetFormat;
                if (!targetFormat) {
                    // DAT is absent on purpose: it is the game's load format, produced by Export to DAT.
                    const picked = await vscode.window.showQuickPick(
                        [
                            {
                                label: vscode.l10n.t("CSV"),
                                description: vscode.l10n.t(
                                    "Comma-separated values (.csv)",
                                ),
                            },
                            {
                                label: vscode.l10n.t("XML"),
                                description: vscode.l10n.t(
                                    "eaw-translation v1 XML (.xml)",
                                ),
                            },
                            {
                                label: vscode.l10n.t("NLS"),
                                description: vscode.l10n.t(
                                    "Java-style properties (.properties)",
                                ),
                            },
                        ],
                        {
                            title: vscode.l10n.t("Convert Localisation File"),
                            placeHolder: vscode.l10n.t(
                                "Write it in which format?",
                            ),
                        },
                    );
                    if (!picked) {
                        return;
                    }
                    targetFormat = picked.label;
                }

                const result = await lsp.requestOrReport(
                    "aet/convertLocalisationFormat",
                    { projectFilePath: filePath, targetFormat },
                    vscode.l10n.t(
                        "Could not convert {0} to {1}",
                        path.basename(filePath),
                        targetFormat,
                    ),
                );
                if (!result) {
                    return;
                }

                if (result.error) {
                    vscode.window.showErrorMessage(
                        vscode.l10n.t("EaWEdit: {0}", result.error),
                    );
                    return;
                }

                // A repoint leaves everything still in the old format unloaded. Worth saying now,
                // while it is one line of the .pgproj away from being put back.
                const orphans =
                    result.projectFormatChanged &&
                    result.otherFilesInOldFormat > 0
                        ? result.otherFilesInOldFormat
                        : 0;
                // A single-language target (NLS) writes one file per language rather than dropping
                // all but one, so the message has to be able to report more than a single path.
                const written =
                    result.writtenPaths ??
                    (result.writtenPath ? [result.writtenPath] : []);
                // Whole sentences, one per case: clauses glued into one give a translator nothing to work with.
                const names = written.map((p) => path.basename(p)).join(", ");
                const message =
                    written.length > 1
                        ? orphans > 0
                            ? vscode.l10n.t(
                                  "EaWEdit: Wrote {0} files, one per language: {1}. The original file was kept. {2} other file(s) are still in the previous format and are no longer loaded.",
                                  written.length,
                                  names,
                                  orphans,
                              )
                            : vscode.l10n.t(
                                  "EaWEdit: Wrote {0} files, one per language: {1}. The original file was kept.",
                                  written.length,
                                  names,
                              )
                        : orphans > 0
                          ? vscode.l10n.t(
                                "EaWEdit: Wrote {0}. The original file was kept. {1} other file(s) are still in the previous format and are no longer loaded.",
                                written[0],
                                orphans,
                            )
                          : vscode.l10n.t(
                                "EaWEdit: Wrote {0}. The original file was kept.",
                                written[0],
                            );
                const choice = await vscode.window.showInformationMessage(
                    message,
                    vscode.l10n.t("Open"),
                );
                localisationNavigatorProvider?.refresh();
                if (choice === vscode.l10n.t("Open") && result.writtenPath) {
                    LocalisationEditorPanel.show(
                        result.writtenPath,
                        path.basename(result.writtenPath),
                        category ?? LOC_CATEGORY.text,
                        context.extensionUri,
                        panelProject(projectKey),
                    );
                }
            },
        ),
        // Which format the project LOADS (#121), as opposed to converting a file. The palette entry and
        // the navigator's title-bar button are this one command. Nothing is converted: the .pgproj is
        // repointed and the project reloads, so the message has to say how many files that leaves.
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.setLocalisationProjectFormat",
            async () => {
                const projectKey = await commandProjectKey();
                if (projectKey === null) {
                    return;
                }
                const lsp = gatewayFor(projectKey);
                if (!lsp.requireRunning()) {
                    return;
                }

                const config = await lsp.request(
                    "aet/getRootLocalisationConfig",
                );
                const current =
                    config.ok && config.value.configured
                        ? config.value.type
                        : null;

                const picked = await vscode.window.showQuickPick(
                    formatChoices(current),
                    {
                        title: vscode.l10n.t("Set Localisation Project Format"),
                        placeHolder: vscode.l10n.t(
                            "Load the project's text from which format? No files are converted",
                        ),
                    },
                );
                if (!picked) {
                    return;
                }

                const result = await lsp.requestOrReport(
                    "aet/setLocalisationProjectFormat",
                    { format: picked.format },
                    vscode.l10n.t(
                        "Could not set the localisation format to {0}",
                        picked.format,
                    ),
                );
                if (!result) {
                    return;
                }

                const outcome = formatOutcome(result);
                if (outcome.kind === "error") {
                    vscode.window.showErrorMessage(outcome.text);
                    return;
                }
                if (outcome.kind === "warning") {
                    vscode.window.showWarningMessage(outcome.text);
                } else {
                    vscode.window.showInformationMessage(outcome.text);
                }
                if (result.changed) {
                    localisationNavigatorProvider?.refresh();
                }
            },
        ),
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.exportLocalisationToDat",
            async (arg?: LocTreeItem & { projectKey?: string }) => {
                const filePath = arg?.project?.filePath;
                if (!filePath) {
                    vscode.window.showWarningMessage(
                        vscode.l10n.t(
                            "EaWEdit: No localisation file selected.",
                        ),
                    );
                    return;
                }
                const projectKey = await locCommandProjectKey(arg);
                if (projectKey === null) {
                    return;
                }
                const lsp = gatewayFor(projectKey);
                if (!lsp.requireRunning()) {
                    return;
                }

                const result = await lsp.requestOrReport(
                    "aet/exportLocalisationToDat",
                    { projectFilePath: filePath },
                    vscode.l10n.t("DAT export failed"),
                );
                if (!result) {
                    return;
                }

                if (result.error) {
                    vscode.window.showErrorMessage(
                        vscode.l10n.t(
                            "EaWEdit: DAT export failed - {0}",
                            result.error,
                        ),
                    );
                    return;
                }
                vscode.window.showInformationMessage(
                    result.writtenFiles.length === 1
                        ? vscode.l10n.t("EaWEdit: Exported 1 DAT file.")
                        : vscode.l10n.t(
                              "EaWEdit: Exported {0} DAT files.",
                              result.writtenFiles.length,
                          ),
                );
            },
        ),
    );

    context.subscriptions.push(
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.createLocalisationKey",
            async (keyName?: string) => {
                if (!keyName) {
                    vscode.window.showWarningMessage(
                        vscode.l10n.t(
                            "EaWEdit: No localisation key name provided.",
                        ),
                    );
                    return;
                }

                // The quick fix raising this belongs to the document in front of the reader.
                const projectKey = await commandProjectKey();
                if (projectKey === null) {
                    return;
                }
                const lsp = gatewayFor(projectKey);
                const fetched = await lsp.request(
                    "aet/getLocalisationProjects",
                );
                if (!fetched.ok) {
                    vscode.window.showWarningMessage(
                        vscode.l10n.t(
                            "EaWEdit: Could not fetch localisation projects from server.",
                        ),
                    );
                    return;
                }
                const projects = fetched.value.projects ?? [];

                if (!projects.length) {
                    vscode.window.showWarningMessage(
                        vscode.l10n.t(
                            "EaWEdit: No localisation projects found. Use 'EaWEdit: Initialise Localisation Project from Baseline' first.",
                        ),
                    );
                    return;
                }

                const picked = await vscode.window.showQuickPick(
                    projects.map((p) => ({
                        label: p.label,
                        description: p.filePath,
                        detail: `${p.projectName} - ${p.resourceType}`,
                        filePath: p.filePath,
                    })),
                    {
                        title: vscode.l10n.t(
                            "Create localisation key '{0}'",
                            keyName,
                        ),
                        placeHolder: vscode.l10n.t(
                            "Select localisation project",
                        ),
                    },
                );
                if (!picked) {
                    return;
                }

                const english = await vscode.window.showInputBox({
                    title: vscode.l10n.t("New key: {0}", keyName),
                    prompt: vscode.l10n.t(
                        "English translation text (required)",
                    ),
                    validateInput: (v) =>
                        v?.trim()
                            ? null
                            : vscode.l10n.t("Translation text is required"),
                });
                if (english === undefined) {
                    return;
                }

                await lsp.executeCommand(
                    "aet-eaw-edit.lsp.createLocalisationKey",
                    [keyName, picked.filePath, { ENGLISH: english }],
                    l10n.t(
                        "could not create the localisation key '{0}'",
                        keyName,
                    ),
                );
            },
        ),
    );

    context.subscriptions.push(
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.initLocalisationProject",
            async () => {
                const projectKey = await commandProjectKey();
                if (projectKey === null) {
                    return;
                }
                const lsp = gatewayFor(projectKey);
                if (!lsp.requireRunning()) {
                    return;
                }

                const config = await lsp.request(
                    "aet/getRootLocalisationConfig",
                );
                if (!config.ok) {
                    vscode.window.showWarningMessage(
                        vscode.l10n.t(
                            "EaWEdit LSP: Could not query the project's localisation config.",
                        ),
                    );
                    return;
                }
                const rootConfig = config.value;

                // The .pgproj already declares a localisation node - it wins outright, no picker.
                if (rootConfig.configured) {
                    const confirmed =
                        await vscode.window.showInformationMessage(
                            vscode.l10n.t(
                                'Initialise the localisation project ({0} in "{1}")?',
                                rootConfig.type ?? "",
                                rootConfig.directory ?? "",
                            ),
                            { modal: true },
                            vscode.l10n.t("Initialise"),
                        );
                    if (confirmed !== vscode.l10n.t("Initialise")) {
                        return;
                    }
                    await lsp.executeCommand(
                        "aet-eaw-edit.lsp.initLocalisationProject",
                        [{}],
                        l10n.t("could not initialise the localisation project"),
                    );
                    return;
                }

                // Not configured - the VS Code setting only pre-fills the picker here, as a last resort.
                const formatOptions = [
                    {
                        label: vscode.l10n.t("CSV"),
                        description: vscode.l10n.t(
                            "Comma-separated values (.csv)",
                        ),
                    },
                    {
                        label: vscode.l10n.t("XML"),
                        description: vscode.l10n.t(
                            "eaw-translation v1 XML (.xml)",
                        ),
                    },
                    {
                        label: vscode.l10n.t("NLS"),
                        description: vscode.l10n.t(
                            "Java-style properties (.properties)",
                        ),
                    },
                ];
                const defaultFormat = cfg("localisation")
                    .get<string>("format", "format-dat")
                    .replace(/^format-/, "")
                    .toUpperCase();
                const defaultIdx = formatOptions.findIndex(
                    (o) => o.label === defaultFormat,
                );
                if (defaultIdx > 0) {
                    formatOptions.unshift(
                        formatOptions.splice(defaultIdx, 1)[0],
                    );
                }

                const formatItem = await vscode.window.showQuickPick(
                    formatOptions,
                    {
                        title: vscode.l10n.t(
                            "Initialise Localisation Project from Baseline",
                        ),
                        placeHolder: vscode.l10n.t("Select output format"),
                    },
                );
                if (!formatItem) {
                    return;
                }

                const directory = await vscode.window.showInputBox({
                    title: vscode.l10n.t(
                        "Localisation directory (relative to the .pgproj)",
                    ),
                    value: "data/text",
                    prompt: vscode.l10n.t(
                        "Where the localisation project files will live",
                    ),
                    validateInput: (v) =>
                        v?.trim()
                            ? null
                            : vscode.l10n.t("A directory is required"),
                });
                if (!directory) {
                    return;
                }

                await lsp.executeCommand(
                    "aet-eaw-edit.lsp.initLocalisationProject",
                    [{ format: formatItem.label, directory: directory.trim() }],
                    l10n.t("could not initialise the localisation project"),
                );
            },
        ),
    );

    context.subscriptions.push(
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.importLocalisationProject",
            async () => {
                const projectKey = await commandProjectKey();
                if (projectKey === null) {
                    return;
                }
                const lsp = gatewayFor(projectKey);
                if (!lsp.requireRunning()) {
                    return;
                }

                const convertibleFormats = [
                    {
                        label: vscode.l10n.t("CSV"),
                        description: vscode.l10n.t(
                            "Comma-separated values (.csv)",
                        ),
                    },
                    {
                        label: vscode.l10n.t("XML"),
                        description: vscode.l10n.t(
                            "eaw-translation v1 XML (.xml)",
                        ),
                    },
                    {
                        label: vscode.l10n.t("NLS"),
                        description: vscode.l10n.t(
                            "Java-style properties (.properties)",
                        ),
                    },
                ];
                const datFormat = {
                    label: vscode.l10n.t("DAT"),
                    description: vscode.l10n.t(
                        "The game's proprietary binary format (.dat)",
                    ),
                };
                const sourceFormatChoices = [...convertibleFormats, datFormat];

                const sourceFormatItem = await vscode.window.showQuickPick(
                    sourceFormatChoices,
                    {
                        title: vscode.l10n.t(
                            "Import Existing Localisation Files",
                        ),
                        placeHolder: vscode.l10n.t(
                            "Format of the existing files",
                        ),
                    },
                );
                if (!sourceFormatItem) {
                    return;
                }
                const isDatSource =
                    sourceFormatItem.label === vscode.l10n.t("DAT");

                // DAT is one binary file per language (MasterTextFile_<LANGUAGE>.dat) - the user picks
                // one file, the server discovers its siblings in the same folder. Every other format
                // stores all languages in one file, so the user picks the containing folder directly.
                let sourceDirectory: string;
                const defaultUri = vscode.workspace.workspaceFolders?.[0]?.uri;
                if (isDatSource) {
                    const sourceFiles = await vscode.window.showOpenDialog({
                        canSelectFolders: false,
                        canSelectFiles: true,
                        canSelectMany: false,
                        defaultUri,
                        filters: { [vscode.l10n.t("DAT files")]: ["dat"] },
                        openLabel: vscode.l10n.t(
                            "Select a MasterTextFile_<LANGUAGE>.dat file",
                        ),
                        title: vscode.l10n.t(
                            "Select one DAT file - its siblings in the same folder are imported too",
                        ),
                    });
                    if (!sourceFiles?.length) {
                        return;
                    }
                    sourceDirectory = vscode.Uri.joinPath(
                        sourceFiles[0],
                        "..",
                    ).fsPath;
                } else {
                    const sourceFolders = await vscode.window.showOpenDialog({
                        canSelectFolders: true,
                        canSelectFiles: false,
                        canSelectMany: false,
                        defaultUri,
                        openLabel: vscode.l10n.t(
                            "Select folder with existing files",
                        ),
                        title: vscode.l10n.t(
                            "Folder containing the {0} files",
                            sourceFormatItem.label,
                        ),
                    });
                    if (!sourceFolders?.length) {
                        return;
                    }
                    sourceDirectory = sourceFolders[0].fsPath;
                }

                // Pre-fill the target format to match the source - the common case is "just register
                // what I already have," not a conversion. DAT is never a conversion target (no DAT
                // generator wired into this wizard - use the existing "Export DAT" action for that), so
                // it only appears as a target choice when the source is DAT itself, with no pre-selection
                // (DAT is never git-friendly for collaborative editing - force an explicit choice).
                let targetFormatItem:
                    { label: string; description: string } | undefined;
                if (isDatSource) {
                    targetFormatItem = await vscode.window.showQuickPick(
                        [...convertibleFormats, datFormat],
                        {
                            title: vscode.l10n.t("Target Format"),
                            placeHolder: vscode.l10n.t(
                                "Format to use going forward",
                            ),
                        },
                    );
                } else {
                    const targetChoices = convertibleFormats.map((o) => ({
                        ...o,
                    }));
                    const sourceIdx = targetChoices.findIndex(
                        (o) => o.label === sourceFormatItem.label,
                    );
                    if (sourceIdx > 0) {
                        targetChoices.unshift(
                            targetChoices.splice(sourceIdx, 1)[0],
                        );
                    }
                    targetFormatItem = await vscode.window.showQuickPick(
                        targetChoices,
                        {
                            title: vscode.l10n.t("Target Format"),
                            placeHolder: vscode.l10n.t(
                                "Format to use going forward (defaults to {0})",
                                sourceFormatItem.label,
                            ),
                        },
                    );
                }
                if (!targetFormatItem) {
                    return;
                }

                let targetDirectory: string | undefined;
                if (targetFormatItem.label !== sourceFormatItem.label) {
                    targetDirectory = await vscode.window.showInputBox({
                        title: vscode.l10n.t(
                            "Target directory (relative to the .pgproj)",
                        ),
                        value: "data/text",
                        prompt: vscode.l10n.t(
                            "Converting {0} to {1} - where should the new file go?",
                            sourceFormatItem.label,
                            targetFormatItem.label,
                        ),
                        validateInput: (v) =>
                            v?.trim()
                                ? null
                                : vscode.l10n.t("A directory is required"),
                    });
                    if (!targetDirectory) {
                        return;
                    }
                }

                await lsp.executeCommand(
                    "aet-eaw-edit.lsp.importLocalisationProject",
                    [
                        {
                            sourceFormat: sourceFormatItem.label,
                            sourceDirectory,
                            targetFormat: targetFormatItem.label,
                            ...(targetDirectory
                                ? { targetDirectory: targetDirectory.trim() }
                                : {}),
                        },
                    ],
                    l10n.t("could not import the localisation files"),
                );
                // No message here: executeCommand returns nothing to check, so anything said at this
                // point is a guess. The server reports the outcome - what it wrote, or why it did not.
            },
        ),
    );
}
