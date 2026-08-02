// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The panel's view of a tab's unsaved work, kept free of `vscode` so the rules can be unit-tested.
// The webview owns the staged queue and mirrors it here, because a disposed webview cannot prompt
// for its own close - by the time the tab is going away, this is the only copy left.

export class LocalisationPanelState {
    private _pending: Record<string, unknown>[] = [];
    private _contentHash: string | undefined;
    /** The hash of the contents the webview was last handed. See {@link shouldDeliver}. */
    private _deliveredHash: string | undefined;

    /** The staged commands, in the order the user performed them. */
    get pending(): Record<string, unknown>[] {
        return this._pending;
    }

    get pendingCount(): number {
        return this._pending.length;
    }

    get isDirty(): boolean {
        return this._pending.length > 0;
    }

    /**
     * Hash of the text this tab last read, echoed on the next save so the server can tell whether
     * the file moved underneath it.
     */
    get contentHash(): string | undefined {
        return this._contentHash;
    }

    /**
     * Mirrors the webview's queue. A replacement rather than an append: the webview owns the queue
     * and re-sends it whole, so accumulating here would double-count every edit.
     */
    syncPending(commands: Record<string, unknown>[]): void {
        this._pending = commands;
    }

    /**
     * A fresh read of the file. Staged edits are dropped with the hash they were built against -
     * their row indices refer to the old contents and would land on the wrong rows.
     */
    noteRead(contentHash: string | undefined): void {
        this._contentHash = contentHash;
        this._deliveredHash = contentHash;
        this._pending = [];
    }

    /**
     * Whether a freshly read file is worth handing to the webview.
     *
     * A save makes the server reload its localisation index, and the file watcher then reports the
     * server's own write - so a single save announced "the index moved" twice, and every open tab
     * re-read its file and re-rendered every row for a document that had not changed. That also
     * resets the tab (selection, sort, the inherited toggle) and costs a second baseline fetch, so
     * it is felt, not merely wasteful.
     *
     * A read with no hash always goes through: there is nothing to compare, and suppressing it
     * would leave the tab showing nothing.
     */
    shouldDeliver(contentHash: string | undefined): boolean {
        return contentHash === undefined || contentHash !== this._deliveredHash;
    }

    /**
     * A save that landed. The returned hash becomes the basis for the next one; without adopting it
     * the second save of a session would always be refused as stale. A save that returns no hash
     * leaves the previous one in place, since dropping to undefined would be rejected outright as
     * "no content hash provided".
     */
    noteSaved(newContentHash: string | undefined): void {
        if (newContentHash) {
            this._contentHash = newContentHash;
            // The webview already shows this state - it is what it just asked to be written - so
            // the watcher echo that follows must not be taken for someone else's edit.
            this._deliveredHash = newContentHash;
        }

        this._pending = [];
    }

    /** A save that did not land. The queue and hash stay exactly as they were. */
    noteSaveFailed(): void {
        // Intentionally empty: the point is that nothing changes. Named so the call site reads as a
        // decision rather than an omission.
    }

    shouldPromptOnClose(): boolean {
        return this.isDirty;
    }
}
