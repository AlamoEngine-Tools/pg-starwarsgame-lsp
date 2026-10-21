// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import * as vscode from "vscode";

import { PanelProject } from "./panelProject";
import { MOD_BROWSER_METHODS, SymbolOutline } from "./protocol/modBrowser";
import {
    PanelRegistry,
    WebviewMessage,
    WebviewPanelHost,
} from "./webviewPanelHost";

/**
 * The Mod Browser (#270): one panel per project, showing whichever outline was picked last.
 *
 * The webview asks for its lists and entries itself, one page at a time, and the panel passes each
 * request to its project's server - so a list of thousands is never held here, and paging stays the
 * server's.
 */
export class ModBrowserPanel extends WebviewPanelHost {
    /** The open panels, at most one per project - filed under the empty subject. */
    private static readonly _panels = new PanelRegistry<ModBrowserPanel>();

    private _ready = false;
    private _outline: SymbolOutline;

    private constructor(
        extensionUri: vscode.Uri,
        project: PanelProject,
        outline: SymbolOutline,
    ) {
        super(extensionUri, {
            project,
            viewType: "aetModBrowser",
            title: vscode.l10n.t("Mod Browser"),
            column: vscode.ViewColumn.Active,
            script: "modBrowser.js",
            bodyStyle:
                "  html, body { margin: 0; padding: 0; height: 100%; }\n" +
                "  #root { height: 100%; }",
        });
        this._outline = outline;
    }

    /** Opens the project's panel on an outline, or turns the open one to it. */
    static show(
        extensionUri: vscode.Uri,
        project: PanelProject,
        outline: SymbolOutline,
    ): void {
        const open = ModBrowserPanel._panels.get(project.key, "");
        const panel =
            open ??
            ModBrowserPanel._panels.track(
                "",
                new ModBrowserPanel(extensionUri, project, outline),
            );
        panel._outline = outline;
        panel.reveal();
        panel._sendOutline();
    }

    /** Re-reads what the project's panel shows, once the server's index has changed. */
    static refresh(projectKey: string): void {
        for (const panel of ModBrowserPanel._panels.forProject(projectKey)) {
            if (panel._ready) {
                panel.post({ type: "refresh" });
            }
        }
    }

    static disposeAll(): void {
        ModBrowserPanel._panels.disposeAll();
    }

    protected async onMessage(msg: WebviewMessage): Promise<void> {
        if (msg.type === "ready") {
            this._ready = true;
            this._sendOutline();
        }
        if (msg.type === "request") {
            await this._relay(msg);
        }
    }

    private _sendOutline(): void {
        if (this._ready) {
            this.post({ type: "outline", outline: this._outline });
        }
    }

    /** Passes one of the browser's requests to the server; anything else is answered with nothing. */
    private async _relay(msg: WebviewMessage): Promise<void> {
        const method = typeof msg.method === "string" ? msg.method : "";
        const outcome = MOD_BROWSER_METHODS.includes(method)
            ? await this.project.gateway.request<unknown>(method, msg.params)
            : undefined;
        this.post({
            type: "response",
            id: msg.id,
            result: outcome?.ok ? outcome.value : null,
        });
    }
}
