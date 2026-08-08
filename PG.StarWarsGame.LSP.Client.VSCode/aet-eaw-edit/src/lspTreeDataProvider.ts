// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What both navigators are made of: a server-fed tree that preloads, refreshes and says something
// useful when it has nothing to show.
//
// The two had written this twice and already diverged on the part that matters - how the fetched
// data is cached. One used `T[] | undefined`, the other a `boolean` flag beside a separate array,
// which is the same state machine with an extra variable that can disagree with the first. Both
// also spelled out the identical four-branch load: no server, error, empty, items.

import * as vscode from 'vscode';

import { LspGateway, LspOutcome } from './lsp/lspGateway';
import { emptyNavigatorMessage } from './navigatorPlaceholder';

/**
 * A tree view fed by one `aet/*` request.
 *
 * @typeParam TItem The view's TreeItem type.
 * @typeParam TData What the request returns for the tree to build itself from.
 */
export abstract class LspTreeDataProvider<TItem extends vscode.TreeItem, TData>
implements vscode.TreeDataProvider<TItem> {
    private readonly _onDidChangeTreeData = new vscode.EventEmitter<TItem | undefined>();
    readonly onDidChangeTreeData = this._onDidChangeTreeData.event;

    /**
     * Whether the workspace scan has finished.
     *
     * Until it has, an empty answer from the server means "not indexed yet", not "this workspace
     * has none" - and both trees used to say the latter, then correct themselves a moment later
     * when the index landed, which reads as the view being wrong twice.
     */
    private _scanned = false;

    /**
     * The last answer, or undefined for "not fetched, or the last fetch failed".
     *
     * One variable, so there is no second one to fall out of step with it. Cleared by
     * {@link refresh} so the next read is fresh.
     */
    protected data: TData | undefined;

    protected constructor(protected readonly lsp: LspGateway) {}

    // ── what a subclass supplies ─────────────────────────────────────────────

    /** The `aet/*` method this tree is built from. */
    protected abstract readonly method: string;

    /** What the view is showing, for the empty-state line: "localisation files", "story campaigns". */
    protected abstract readonly subject: string;

    /** The error the payload carries, if any. Server results report failure in-band. */
    protected abstract errorOf(data: TData): string | null | undefined;

    /** Whether the answer is empty - which is a different thing from a failure. */
    protected abstract isEmpty(data: TData): boolean;

    /** Builds the root rows from a non-empty answer. */
    protected abstract rootItems(data: TData): TItem[];

    /** A plain row for the empty, error and server-down states, so the view is never blank. */
    protected abstract infoItem(message: string): TItem;

    /** The children of a row. Roots are handled here; this is only ever asked about a real item. */
    protected abstract childrenOf(element: TItem): TItem[] | Promise<TItem[]>;

    // ── the shared behaviour ─────────────────────────────────────────────────

    refresh(): void {
        this.data = undefined;
        this._onDidChangeTreeData.fire(undefined);
    }

    /** Repaints without discarding what is cached - for a change of presentation, not of data. */
    protected repaint(): void {
        this._onDidChangeTreeData.fire(undefined);
    }

    /**
     * Fetches in the background and repaints.
     *
     * Called when the workspace scan completes, so the view is ready before it is looked at: a tree
     * view only asks for its children when it is first revealed, so without this the first click on
     * either navigator paid for a round trip - and until the scan finished they had nothing to show
     * but a "loading" line.
     */
    async preload(): Promise<void> {
        this._scanned = true;
        if (!this.lsp.isRunning) { return; }

        // Left uncached on any failure. The view then fetches when it is opened, which is where a
        // failure has somewhere to be shown - here there is no row to put it in.
        const outcome = await this.lsp.request<TData>(this.method);
        this.data = outcome.ok && !this.errorOf(outcome.value) ? outcome.value : undefined;

        this._onDidChangeTreeData.fire(undefined);
    }

    getTreeItem(element: TItem): vscode.TreeItem {
        return element;
    }

    async getChildren(element?: TItem): Promise<TItem[]> {
        return element === undefined ? this._loadRoot() : this.childrenOf(element);
    }

    private async _loadRoot(): Promise<TItem[]> {
        if (this.data !== undefined) { return this._present(this.data); }

        const outcome = await this.lsp.request<TData>(this.method);
        if (!outcome.ok) { return [this.infoItem(this.failureMessage(outcome))]; }

        const error = this.errorOf(outcome.value);
        if (error) { return [this.infoItem(error)]; }

        this.data = outcome.value;
        return this._present(outcome.value);
    }

    private _present(data: TData): TItem[] {
        return this.isEmpty(data)
            ? [this.infoItem(emptyNavigatorMessage(this._scanned, this.subject))]
            : this.rootItems(data);
    }

    /**
     * What to put in the row when the request did not come back.
     *
     * Overridable because the two navigators word a broken request differently - one names the
     * error, the other keeps it short - and neither is wrong.
     */
    protected failureMessage(outcome: Extract<LspOutcome<TData>, { ok: false }>): string {
        return outcome.reason === 'offline'
            ? 'LSP server is not running.'
            : `Could not read ${this.subject} from the server.`;
    }
}
