// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The commands a diagnostic offers, and the bug report.

import * as l10n from "@vscode/l10n";
import * as vscode from "vscode";

import {
    type BugReportClientInfo,
    type BugReportOptions,
    flattenFlags,
    formatBugReport,
    formatExtendedStats,
} from "../bugReport";
import {
    type DiagnosticReportPayload,
    editorReport,
    gitHubIssue,
} from "../diagnosticReport";
import { resolveFeatureFlags } from "../hostSettings";
import { logFolderLabel, workspaceIdOf } from "../logStore";
import { LspGateway } from "../lsp/lspGateway";
import { type ServerStatus } from "../protocol/serverStatus";

import type { HostDeps } from "./host";

/**
 * The basic bug report block: what the client knows about itself, plus the server's own status
 * when it is running. Outcomes only - no path and no project name - because it goes into public
 * issues.
 */
async function collectBugReport(
    context: vscode.ExtensionContext,
    gateway: LspGateway,
    options: BugReportOptions = {},
): Promise<string> {
    const { client, server } = await reportInputs(
        context,
        gateway,
        options.extended === true,
    );
    return formatBugReport(client, server, options);
}

/** What the client knows about itself, and the server's status when it is running. */
async function reportInputs(
    context: vscode.ExtensionContext,
    gateway: LspGateway,
    extended: boolean,
): Promise<{ client: BugReportClientInfo; server: ServerStatus | null }> {
    const client: BugReportClientInfo = {
        extensionVersion: String(
            context.extension.packageJSON.version ?? "unknown",
        ),
        vscodeVersion: vscode.version,
        os: `${process.platform} ${process.arch}`,
        workspaceFolders: vscode.workspace.workspaceFolders?.length ?? 0,
        workspaceFile: vscode.workspace.workspaceFile !== undefined,
        logFolder: logFolderLabel(workspaceIdOf(context.storageUri?.fsPath)),
        featureFlags: flattenFlags(resolveFeatureFlags()),
    };

    const status = gateway.isRunning
        ? await gateway.request("aet/getServerStatus", {
              extended,
          })
        : undefined;

    return { client, server: status?.ok ? status.value : null };
}

/**
 * Basic or basic plus extended stats - asked every time rather than remembered, because the
 * extended counts describe the project's shape and sharing them is a choice made per report.
 * Undefined when dismissed.
 */
async function pickReportTier(
    title: string,
): Promise<{ extended: boolean } | undefined> {
    return vscode.window.showQuickPick(
        [
            {
                label: vscode.l10n.t("Basic info"),
                description: vscode.l10n.t(
                    "Versions, sources and workspace checks",
                ),
                extended: false,
            },
            {
                label: vscode.l10n.t("Basic + extended stats"),
                description: vscode.l10n.t(
                    "Adds dependency, index and asset counts",
                ),
                extended: true,
            },
        ],
        { title, placeHolder: vscode.l10n.t("What to include") },
    );
}

/**
 * Opens a prefilled GitHub issue for one diagnostic. The author reviews and submits it in the
 * browser; nothing is posted from here, which is why no login is needed.
 */
async function reportDiagnosticOnGitHub(
    context: vscode.ExtensionContext,
    gateway: LspGateway,
    report: DiagnosticReportPayload,
): Promise<void> {
    const tier = await pickReportTier(`Report ${report.id} on GitHub`);
    if (!tier) {
        return;
    }

    const { client, server } = await reportInputs(
        context,
        gateway,
        tier.extended,
    );
    // The per-type symbol list stays out: it runs to dozens of lines and the prefill is capped.
    const extended =
        tier.extended && server?.extended
            ? formatExtendedStats(server.extended, false)
            : null;
    const issue = gitHubIssue(
        report,
        formatBugReport(client, server),
        extended,
    );

    if (!issue.fits) {
        const choice = await vscode.window.showWarningMessage(
            vscode.l10n.t(
                "EaWEdit: The report for {0} is too long to prefill an issue",
                report.id,
            ),
            vscode.l10n.t("Open report in editor"),
        );
        if (choice === vscode.l10n.t("Open report in editor")) {
            await openDiagnosticReport(context, gateway, report, tier);
        }
        return;
    }

    await vscode.env.openExternal(vscode.Uri.parse(issue.url));

    if (issue.trimmed.length > 0) {
        const left = issue.trimmed.map((t) =>
            t === "extended"
                ? vscode.l10n.t("extended stats")
                : vscode.l10n.t("part of the basic info"),
        );
        const choice = await vscode.window.showInformationMessage(
            vscode.l10n.t(
                "EaWEdit: Issue prefill shortened - {0} left out",
                left.join(vscode.l10n.t(" and ")),
            ),
            vscode.l10n.t("Open full report"),
        );
        if (choice === vscode.l10n.t("Open full report")) {
            await openDiagnosticReport(context, gateway, report, tier);
        }
    }
}

/** The same report, untrimmed, in an untitled Markdown tab - for pasting anywhere. */
async function openDiagnosticReport(
    context: vscode.ExtensionContext,
    gateway: LspGateway,
    report: DiagnosticReportPayload,
    chosen?: { extended: boolean },
): Promise<void> {
    const tier =
        chosen ??
        (await pickReportTier(vscode.l10n.t("Open report for {0}", report.id)));
    if (!tier) {
        return;
    }

    const { client, server } = await reportInputs(
        context,
        gateway,
        tier.extended,
    );
    const full = formatBugReport(client, server, {
        extended: tier.extended,
        symbolTypes: tier.extended,
    });
    const document = await vscode.workspace.openTextDocument({
        language: "markdown",
        content: editorReport(report, full),
    });
    await vscode.window.showTextDocument(document);
}

export function register(
    context: vscode.ExtensionContext,
    deps: HostDeps,
): void {
    const { clients, commandProjectKey, gatewayFor, lsp, ownerOf } = deps;

    // The server's remaining commands, owned here like the rest. The language client would register
    // them from the server's one `registerCapability` batch, but that batch also names commands this
    // extension registers itself, collides, and is dropped whole - so a quick fix naming one of these
    // found no command at all.
    context.subscriptions.push(
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.suppressDiagnosticGlobally",
            async (...args: unknown[]) => {
                const projectKey = await commandProjectKey();
                if (projectKey === null) {
                    return;
                }
                await gatewayFor(projectKey).executeCommand(
                    "aet-eaw-edit.lsp.suppressDiagnosticGlobally",
                    args,
                    l10n.t("could not suppress the diagnostic"),
                );
            },
        ),
    );

    context.subscriptions.push(
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.revalidateDocument",
            async (uri?: string) => {
                const target =
                    uri ??
                    vscode.window.activeTextEditor?.document.uri.toString();
                if (target === undefined) {
                    return;
                }
                const owner = ownerOf(vscode.Uri.parse(target).fsPath);
                const projectKey =
                    owner === undefined || clients.size <= 1
                        ? await commandProjectKey()
                        : owner.spec.key;
                if (projectKey === null) {
                    return;
                }
                await gatewayFor(projectKey).executeCommand(
                    "aet-eaw-edit.lsp.revalidateDocument",
                    [target],
                    l10n.t("could not revalidate the document"),
                );
            },
        ),
    );

    // The bug report block. Works with the server down too - "not running" is itself the most
    // useful thing a report can say - so it asks rather than requiring a running server.
    context.subscriptions.push(
        vscode.commands.registerCommand(
            "aet-eaw-edit.copyBugReportInfo",
            async () => {
                const tier = await pickReportTier(
                    vscode.l10n.t("Copy Bug Report Info"),
                );
                if (!tier) {
                    return;
                }

                // The clipboard has no size limit, so the extended copy carries the per-type list too.
                const text = await collectBugReport(
                    context,
                    lsp,
                    tier.extended ? { extended: true, symbolTypes: true } : {},
                );
                await vscode.env.clipboard.writeText(text);
                void vscode.window.showInformationMessage(
                    vscode.l10n.t("EaWEdit: Bug report info copied"),
                );
            },
        ),
    );

    // The two report actions every diagnostic offers beside its suppressions. Client commands the
    // server only names: the version, the clipboard and the browser are all on this side.
    context.subscriptions.push(
        vscode.commands.registerCommand(
            "aet-eaw-edit.reportDiagnosticOnGitHub",
            (report: DiagnosticReportPayload) =>
                reportDiagnosticOnGitHub(context, lsp, report),
        ),
        vscode.commands.registerCommand(
            "aet-eaw-edit.openDiagnosticReport",
            (report: DiagnosticReportPayload) =>
                openDiagnosticReport(context, lsp, report),
        ),
    );
}
