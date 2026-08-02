// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import * as vscode from 'vscode';

/**
 * The credits crawl in its own tab beside the editor, the way a Markdown or LaTeX preview opens.
 *
 * One per credits file, so previewing two files gives two previews rather than one that keeps
 * changing what it is showing. It holds no state of its own: the editor pushes rows in, and this
 * only decides where they are drawn.
 */
export class CreditsPreviewPanel {
    private static readonly _panels = new Map<string, CreditsPreviewPanel>();

    private readonly _panel: vscode.WebviewPanel;
    private _ready = false;
    private _pending: unknown | null = null;

    private constructor(filePath: string, label: string, extensionUri: vscode.Uri) {
        this._panel = vscode.window.createWebviewPanel(
            'aetCreditsPreview', `Preview: ${label}`, vscode.ViewColumn.Beside,
            {
                enableScripts: true,
                retainContextWhenHidden: true,
                localResourceRoots: [
                    vscode.Uri.joinPath(extensionUri, 'out', 'webview'),
                    vscode.Uri.joinPath(extensionUri, 'out', 'codicons'),
                ],
            });

        this._panel.onDidDispose(() => CreditsPreviewPanel._panels.delete(key(filePath)));

        const scriptUri = this._panel.webview.asWebviewUri(
            vscode.Uri.joinPath(extensionUri, 'out', 'webview', 'creditsPreview.js'));
        const codiconUri = this._panel.webview.asWebviewUri(
            vscode.Uri.joinPath(extensionUri, 'out', 'codicons', 'codicon.css'));
        this._panel.webview.html = buildHtml(
            scriptUri, codiconUri, this._panel.webview.cspSource);

        this._panel.webview.onDidReceiveMessage((msg: { type: string }) => {
            if (msg.type === 'ready') {
                // Rows may have arrived before the webview finished loading; hold them until it
                // says it is listening, or the first preview opens empty.
                this._ready = true;
                if (this._pending !== null) {
                    void this._panel.webview.postMessage(this._pending);
                    this._pending = null;
                }
            }
            if (msg.type === 'close') { this._panel.dispose(); }
        });
    }

    /**
     * Shows the crawl for a file, creating the panel or revealing the one already open.
     *
     * Reveals without stealing focus: the user is editing, and the preview updating beside them
     * should not take the caret out of the cell they are typing in.
     */
    static show(
        filePath: string, label: string, extensionUri: vscode.Uri, payload: unknown
    ): void {
        let panel = CreditsPreviewPanel._panels.get(key(filePath));
        if (panel === undefined) {
            panel = new CreditsPreviewPanel(filePath, label, extensionUri);
            CreditsPreviewPanel._panels.set(key(filePath), panel);
        } else {
            panel._panel.reveal(panel._panel.viewColumn, true);
        }

        panel._send(payload);
    }

    /** Pushes new rows to an open preview, and does nothing when there is none. */
    static update(filePath: string, payload: unknown): void {
        CreditsPreviewPanel._panels.get(key(filePath))?._send(payload);
    }

    static isOpen(filePath: string): boolean {
        return CreditsPreviewPanel._panels.has(key(filePath));
    }

    static disposeAll(): void {
        for (const panel of [...CreditsPreviewPanel._panels.values()]) { panel._panel.dispose(); }
    }

    private _send(payload: unknown): void {
        if (this._ready) { void this._panel.webview.postMessage(payload); } else {
            this._pending = payload;
        }
    }
}

/** Windows path casing: two keys for one file would mean two previews of it. */
function key(filePath: string): string {
    return filePath.toLowerCase();
}

function buildHtml(scriptUri: vscode.Uri, codiconUri: vscode.Uri, cspSource: string): string {
    return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none'; style-src 'unsafe-inline' ${cspSource}; script-src ${cspSource}; font-src ${cspSource}; img-src ${cspSource} data:;">
<link rel="stylesheet" href="${codiconUri}">
<style>
  html, body { margin: 0; padding: 0; height: 100%; overflow: hidden; }
  #root { height: 100%; position: relative; }
</style>
</head>
<body>
<div id="root"></div>
<script src="${scriptUri}"></script>
</body>
</html>`;
}
