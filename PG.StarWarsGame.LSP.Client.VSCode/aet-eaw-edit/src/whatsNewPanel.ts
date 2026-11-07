// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The What's New page: the installed version's section of the changelog, rendered in a tab.
//
// The changelog is the one source of the release notes - the Marketplace shows the file, the
// release workflow cuts the version's section out of it for the GitHub release, and this page
// renders the same section - so a change is written once and reaches all three.
//
// Deliberately NOT a WebviewPanelHost. That class binds a panel to one project for its whole
// life, and this page belongs to the extension, not to any mod: it reads a file from the install
// folder, talks to no server, and one tab per window is the right number. It shares the host's
// HTML skeleton through buildWebviewHtml rather than carrying a copy.

import fs from "node:fs";
import path from "node:path";
import * as vscode from "vscode";

import { readPanelLayout } from "./panelLayoutStorage";
import { buildWebviewHtml, WebviewMessage } from "./webviewPanelHost";
import { buildWhatsNewPayload, WhatsNewPayload } from "./whatsNewModel";

/** Opens the page from the palette; the activation hook opens it once per new version. */
export const OPEN_WHATS_NEW = "aet-eaw-edit.whatsNew.open";
export const OPEN_AFTER_UPDATE_SETTING =
    "aet-eaw-edit.whatsNew.openAfterUpdate";
/** Global-state key: the version whose notes the page last opened for, or was told to skip. */
export const WHATS_NEW_SEEN_VERSION = "whatsNew.seenVersion";

/** One per window: a second tab of the same notes would say nothing the first does not. */
export class WhatsNewPanel {
    private static _open: WhatsNewPanel | undefined;

    private readonly _panel: vscode.WebviewPanel;
    private _payload: WhatsNewPayload | undefined;
    private _ready = false;

    private constructor(
        private readonly _extensionUri: vscode.Uri,
        private readonly _version: string,
        private readonly _preview: boolean,
    ) {
        this._panel = vscode.window.createWebviewPanel(
            "aetWhatsNew",
            vscode.l10n.t("What's New in EaWEdit {0}", _version),
            vscode.ViewColumn.Active,
            {
                enableScripts: true,
                retainContextWhenHidden: true,
                enableFindWidget: true,
                localResourceRoots: [
                    vscode.Uri.joinPath(_extensionUri, "out", "webview"),
                    vscode.Uri.joinPath(_extensionUri, "out", "codicons"),
                ],
            },
        );
        const webview = this._panel.webview;
        webview.html = buildWebviewHtml(
            webview.asWebviewUri(
                vscode.Uri.joinPath(
                    _extensionUri,
                    "out",
                    "webview",
                    "whatsNew.js",
                ),
            ),
            webview.asWebviewUri(
                vscode.Uri.joinPath(
                    _extensionUri,
                    "out",
                    "codicons",
                    "codicon.css",
                ),
            ),
            webview.cspSource,
            undefined,
            false,
            readPanelLayout(),
        );
        webview.onDidReceiveMessage((msg: WebviewMessage) =>
            this._onMessage(msg),
        );
        this._panel.onDidDispose(() => {
            if (WhatsNewPanel._open === this) {
                WhatsNewPanel._open = undefined;
            }
        });
    }

    /** Opens the page, or reveals the one already open. */
    static async show(
        extensionUri: vscode.Uri,
        version: string,
        preview: boolean,
    ): Promise<void> {
        let panel = WhatsNewPanel._open;
        if (panel === undefined) {
            panel = new WhatsNewPanel(extensionUri, version, preview);
            WhatsNewPanel._open = panel;
        } else {
            panel._panel.reveal();
        }
        await panel._load();
    }

    static disposeAll(): void {
        WhatsNewPanel._open?._panel.dispose();
    }

    private async _load(): Promise<void> {
        const root = this._extensionUri.fsPath;
        let changelog: string;
        try {
            changelog = await fs.promises.readFile(
                path.join(root, "CHANGELOG.md"),
                "utf8",
            );
        } catch {
            // Nothing to render without the file; the page keeps its loading line, and the
            // Marketplace copy still exists. A missing changelog is a packaging defect, not a
            // state to design for.
            changelog = "";
        }
        this._payload = buildWhatsNewPayload(
            changelog,
            this._version,
            root,
            this._preview,
        );
        this._send();
    }

    private _onMessage(msg: WebviewMessage): void {
        // The one message: the page is listening. Links need nothing here - the webview host
        // opens every web link in the browser by itself.
        if (msg.type === "ready") {
            this._ready = true;
            this._send();
        }
    }

    private _send(): void {
        if (this._ready && this._payload !== undefined) {
            void this._panel.webview.postMessage(this._payload);
        }
    }
}
