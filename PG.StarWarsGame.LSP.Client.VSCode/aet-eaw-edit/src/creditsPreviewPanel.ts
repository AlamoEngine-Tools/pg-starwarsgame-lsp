// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import * as vscode from "vscode";

import { PanelProject } from "./panelProject";
import {
    panelKey,
    PanelRegistry,
    WebviewMessage,
    WebviewPanelHost,
} from "./webviewPanelHost";

/**
 * The credits crawl in its own tab beside the editor, the way a Markdown or LaTeX preview opens.
 *
 * One per credits file, so previewing two files gives two previews rather than one that keeps
 * changing what it is showing. It holds no state of its own: the editor pushes rows in, and this
 * only decides where they are drawn.
 */
export class CreditsPreviewPanel extends WebviewPanelHost {
    private static readonly _panels = new PanelRegistry<CreditsPreviewPanel>();

    private _ready = false;
    private _pending: unknown | null = null;

    private constructor(
        label: string,
        extensionUri: vscode.Uri,
        project: PanelProject,
    ) {
        super(extensionUri, {
            project,
            viewType: "aetCreditsPreview",
            title: vscode.l10n.t("Preview: {0}", label),
            column: vscode.ViewColumn.Beside,
            script: "creditsPreview.js",
            bodyStyle:
                "  html, body { margin: 0; padding: 0; height: 100%; overflow: hidden; }\n" +
                "  #root { height: 100%; position: relative; }",
        });
    }

    /**
     * Shows the crawl for a file, creating the panel or revealing the one already open.
     *
     * Reveals without stealing focus: the user is editing, and the preview updating beside them
     * should not take the caret out of the cell they are typing in.
     */
    static show(
        filePath: string,
        label: string,
        extensionUri: vscode.Uri,
        project: PanelProject,
        payload: unknown,
    ): void {
        const key = panelKey(filePath);
        let panel = CreditsPreviewPanel._panels.get(project.key, key);

        if (panel === undefined) {
            panel = CreditsPreviewPanel._panels.track(
                key,
                new CreditsPreviewPanel(label, extensionUri, project),
            );
        } else {
            panel.reveal(true);
        }

        panel._send(payload);
    }

    /** Pushes new rows to an open preview, and does nothing when there is none. */
    static update(
        project: PanelProject,
        filePath: string,
        payload: unknown,
    ): void {
        CreditsPreviewPanel._panels
            .get(project.key, panelKey(filePath))
            ?._send(payload);
    }

    static isOpen(project: PanelProject, filePath: string): boolean {
        return CreditsPreviewPanel._panels.has(project.key, panelKey(filePath));
    }

    static disposeAll(): void {
        CreditsPreviewPanel._panels.disposeAll();
    }

    protected onMessage(msg: WebviewMessage): void {
        if (msg.type === "ready") {
            // Rows may have arrived before the webview finished loading; hold them until it
            // says it is listening, or the first preview opens empty.
            this._ready = true;
            if (this._pending !== null) {
                this.post(this._pending);
                this._pending = null;
            }
        }
        if (msg.type === "close") {
            this.dispose();
        }
    }

    private _send(payload: unknown): void {
        if (this._ready) {
            this.post(payload);
        } else {
            this._pending = payload;
        }
    }
}
