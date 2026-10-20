// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import * as vscode from "vscode";

import { GetEncyclopediaEntryResult } from "./protocol";
import { PanelProject } from "./panelProject";
import {
    PanelRegistry,
    WebviewMessage,
    WebviewPanelHost,
} from "./webviewPanelHost";
import { pickShipName } from "./shipNamePick";

/**
 * The in-game encyclopedia popup for one GameObject, beside the editor.
 *
 * Deliberately one panel per project that retargets, rather than one keyed per object. The
 * credits preview is keyed per *file* because previewing two files means two different documents;
 * here a units file holds dozens of objects and clicking through them would otherwise leave a
 * trail of tabs. Whichever object was asked for last is the one on screen.
 *
 * One per project rather than one for the window (#276): a single panel reloaded through whichever
 * project had opened it last, so a card asked for in one project could be refreshed from another's
 * index.
 */
export class EncyclopediaPanel extends WebviewPanelHost {
    /** The open panels, at most one per project - filed under the empty subject. */
    private static readonly _panels = new PanelRegistry<EncyclopediaPanel>();

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
    /**
     * The ship name drawn for each object, per object id.
     *
     * The engine picks at random, so there is no "correct" name to show - but re-picking on every
     * response made the card reshuffle whenever anything refreshed it, which reads as flicker. The
     * pick is therefore made once per object and kept for the panel's lifetime: stable while it is
     * open, including across a webview reload when the tab is restored, and freshly drawn when the
     * panel is closed and reopened, since the instance goes with it.
     */
    private readonly _shipNames = new Map<string, string>();

    private constructor(extensionUri: vscode.Uri, project: PanelProject) {
        super(extensionUri, {
            project,
            viewType: "aetEncyclopediaPreview",
            title: vscode.l10n.t("Encyclopedia"),
            column: vscode.ViewColumn.Beside,
            script: "encyclopediaPreview.js",
            bodyStyle:
                "  html, body { margin: 0; padding: 0; height: 100%; }\n" +
                "  #root { height: 100%; }",
        });
    }

    /**
     * Shows an entry, creating the panel or retargeting the open one.
     *
     * Reveals without stealing focus: you are reading XML, and a card appearing beside you should
     * not take the caret out of the file.
     */
    static show(
        extensionUri: vscode.Uri,
        project: PanelProject,
        entry: GetEncyclopediaEntryResult,
        reload: (multiplayer: boolean) => void,
    ): void {
        const panel =
            EncyclopediaPanel._panels.get(project.key, "") ??
            EncyclopediaPanel._panels.track(
                "",
                new EncyclopediaPanel(extensionUri, project),
            );

        panel._reload = reload;
        panel.setTitle(
            entry.displayName?.trim()
                ? vscode.l10n.t("Encyclopedia: {0}", entry.displayName)
                : vscode.l10n.t("Encyclopedia: {0}", entry.objectId),
        );
        panel.reveal(true);
        panel._sendEntry(entry);
    }

    /** Pushes a new entry into the project's open panel, and does nothing when there is none. */
    static update(projectKey: string, entry: GetEncyclopediaEntryResult): void {
        EncyclopediaPanel._panels.get(projectKey, "")?._sendEntry(entry);
    }

    /**
     * Re-fetches whatever the panel is showing, and does nothing when none is open.
     *
     * Everything on the card is resolved against the workspace index - the popup WIDTH above all,
     * which a mod commonly changes. A panel opened while the workspace scan was still running was
     * answered from the baseline alone and then kept that answer: measured on EaWX, vanilla's 262
     * before the scan against the mod's own 340 after it. Nothing re-asked, so the card stayed
     * vanilla for the whole session.
     */
    static refresh(projectKey: string): void {
        for (const panel of EncyclopediaPanel._panels.forProject(projectKey)) {
            panel._reload?.(panel._multiplayer);
        }
    }

    /**
     * The SP/MP mode to fetch a newly targeted object in. False when the project has no panel
     * open, so its first preview always starts single-player.
     */
    static multiplayer(projectKey: string): boolean {
        return (
            EncyclopediaPanel._panels.get(projectKey, "")?._multiplayer ?? false
        );
    }

    static disposeAll(): void {
        EncyclopediaPanel._panels.disposeAll();
    }

    protected onMessage(msg: WebviewMessage): void {
        if (msg.type === "ready") {
            // The entry may have been pushed before the bundle finished loading; hold it until the
            // webview says it is listening, or the first preview opens blank.
            this._ready = true;
            if (this._pending !== null) {
                this.post(this._pending);
                this._pending = null;
            }
        }
        if (msg.type === "setMultiplayer") {
            this._multiplayer = msg.multiplayer === true;
            this._reload?.(this._multiplayer);
        }
        if (msg.type === "close") {
            this.dispose();
        }
    }

    /**
     * Sends an entry together with the mode it was requested in.
     *
     * The mode travels with the entry so the toggle cannot drift out of step with what was
     * fetched - a webview that VS Code reloaded would otherwise come back showing an unticked box
     * over a multiplayer body.
     */
    private _sendEntry(entry: GetEncyclopediaEntryResult): void {
        this._send({
            type: "entry",
            entry,
            multiplayer: this._multiplayer,
            shipName: this._drawShipName(entry),
        });
    }

    /**
     * The name this object is showing, drawn once and then kept - see {@link _shipNames}.
     *
     * Null for the overwhelming majority of objects, which have no pool, and for a pool whose file
     * was missing or empty; the card then keeps the object's class line.
     */
    private _drawShipName(entry: GetEncyclopediaEntryResult): string | null {
        const names = entry.shipNames?.names ?? [];
        if (names.length === 0) {
            return null;
        }

        const kept = this._shipNames.get(entry.objectId);
        // Re-draw if the kept name is no longer in the pool - the author may have edited the file
        // out from under us, and showing a name their data no longer contains would be a lie.
        if (kept !== undefined && names.includes(kept)) {
            return kept;
        }

        const drawn = pickShipName(names);
        if (drawn !== null) {
            this._shipNames.set(entry.objectId, drawn);
        }
        return drawn;
    }

    private _send(payload: unknown): void {
        if (this._ready) {
            this.post(payload);
        } else {
            this._pending = payload;
        }
    }
}
