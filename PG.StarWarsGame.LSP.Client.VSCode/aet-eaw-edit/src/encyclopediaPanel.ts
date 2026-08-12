// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import * as vscode from 'vscode';

import { GetEncyclopediaEntryResult } from './protocol';
import { WebviewMessage, WebviewPanelHost } from './webviewPanelHost';

/**
 * The in-game encyclopedia popup for one GameObject, beside the editor.
 *
 * Deliberately a single panel that retargets, rather than a `PanelRegistry` keyed per object. The
 * credits preview is keyed per *file* because previewing two files means two different documents;
 * here a units file holds dozens of objects and clicking through them would otherwise leave a
 * trail of tabs. Whichever object you asked for last is the one on screen.
 */
export class EncyclopediaPanel extends WebviewPanelHost {
    private static _instance: EncyclopediaPanel | undefined;

    private _ready = false;
    private _pending: unknown | null = null;
    /** Re-fetches the current object, for when the webview toggles between the SP and MP body. */
    private _reload: ((multiplayer: boolean) => void) | undefined;
    /**
     * The SP/MP mode the user last asked for, kept so retargeting to another object preserves it.
     * Without this the command would refetch every new object as single-player while the webview's
     * toggle stayed switched on, showing SP text under an MP label.
     */
    private _multiplayer = false;

    private constructor(extensionUri: vscode.Uri) {
        super(extensionUri, {
            viewType: 'aetEncyclopediaPreview',
            title: 'Encyclopedia',
            column: vscode.ViewColumn.Beside,
            script: 'encyclopediaPreview.js',
            bodyStyle: '  html, body { margin: 0; padding: 0; height: 100%; }\n'
                + '  #root { height: 100%; }',
        });

        this.onDidDispose(() => {
            if (EncyclopediaPanel._instance === this) { EncyclopediaPanel._instance = undefined; }
        });
    }

    protected onMessage(msg: WebviewMessage): void {
        if (msg.type === 'ready') {
            // The entry may have been pushed before the bundle finished loading; hold it until the
            // webview says it is listening, or the first preview opens blank.
            this._ready = true;
            if (this._pending !== null) {
                this.post(this._pending);
                this._pending = null;
            }
        }
        if (msg.type === 'setMultiplayer') {
            this._multiplayer = msg.multiplayer === true;
            this._reload?.(this._multiplayer);
        }
        if (msg.type === 'close') { this.dispose(); }
    }

    /**
     * Shows an entry, creating the panel or retargeting the open one.
     *
     * Reveals without stealing focus: you are reading XML, and a card appearing beside you should
     * not take the caret out of the file.
     */
    static show(
        extensionUri: vscode.Uri,
        entry: GetEncyclopediaEntryResult,
        reload: (multiplayer: boolean) => void,
    ): void {
        const panel = EncyclopediaPanel._instance
            ?? (EncyclopediaPanel._instance = new EncyclopediaPanel(extensionUri));

        panel._reload = reload;
        panel.panel.title = entry.displayName?.trim()
            ? `Encyclopedia: ${entry.displayName}`
            : `Encyclopedia: ${entry.objectId}`;
        panel.reveal(true);
        panel._sendEntry(entry);
    }

    /** Pushes a new entry into an open panel, and does nothing when there is none. */
    static update(entry: GetEncyclopediaEntryResult): void {
        EncyclopediaPanel._instance?._sendEntry(entry);
    }

    static isOpen(): boolean {
        return EncyclopediaPanel._instance !== undefined;
    }

    /**
     * The SP/MP mode to fetch a newly targeted object in. False when no panel is open, so the
     * first preview of a session always starts single-player.
     */
    static multiplayer(): boolean {
        return EncyclopediaPanel._instance?._multiplayer ?? false;
    }

    static disposeAll(): void {
        EncyclopediaPanel._instance?.dispose();
    }

    /**
     * Sends an entry together with the mode it was requested in.
     *
     * The mode travels with the entry so the toggle cannot drift out of step with what was
     * fetched - a webview that VS Code reloaded would otherwise come back showing an unticked box
     * over a multiplayer body.
     */
    private _sendEntry(entry: GetEncyclopediaEntryResult): void {
        this._send({ type: 'entry', entry, multiplayer: this._multiplayer });
    }

    private _send(payload: unknown): void {
        if (this._ready) { this.post(payload); } else { this._pending = payload; }
    }
}
