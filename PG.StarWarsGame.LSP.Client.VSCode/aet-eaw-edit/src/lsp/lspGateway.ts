// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The one place a request reaches the language server.
//
// There were 43 call sites and 19 hand-written "server is not running" guards, and they had already
// stopped agreeing with each other: the story panel wrapped nearly every request in try/catch, the
// localisation panel wrapped only one of five. The four unwrapped ones ran inside a webview message
// handler whose promise nobody awaits, so a server that died mid-edit rejected into nothing - the
// grid stopped saving and said not a word about why.
//
// Kept free of `vscode` so it can be unit-tested: the UI it needs is one two-method sink, and the
// extension host supplies the real one.

/** The two ways a call can come back with nothing. */
export type LspFailure =
    /** The server is not running - nothing was sent. */
    | 'offline'
    /** It was sent and the request rejected, or the server answered with an error. */
    | 'failed';

/**
 * What a call produced.
 *
 * A result object rather than `T | undefined` because the callers genuinely act on the difference:
 * a graph panel with no server posts "LSP server is not running" into its webview, while one whose
 * request failed posts the failure. Collapsing both to `undefined` is what let those two cases
 * drift apart in the first place.
 */
export type LspOutcome<T> =
    | { readonly ok: true; readonly value: T }
    | { readonly ok: false; readonly reason: LspFailure; readonly message: string };

/** Where the gateway's user-facing messages go. `vscode.window` in production. */
export interface LspMessageSink {
    warn(message: string): void;
    error(message: string): void;
}

/** The slice of `LanguageClient` this needs - so a test can supply a function. */
export interface LspRequestSender {
    sendRequest<T>(method: string, params: unknown): Promise<T>;
}

export const SERVER_NOT_RUNNING = 'EaWEdit LSP: server is not running.';

/**
 * The LSP method behind `ExecuteCommandRequest.type`.
 *
 * Spelled out rather than imported so this module stays free of `vscode-languageclient` - which is
 * what lets it be unit-tested. JSON-RPC dispatches on the method name either way, so the typed
 * request object buys nothing a caller here can use.
 */
const EXECUTE_COMMAND = 'workspace/executeCommand';

export class LspGateway {
    /**
     * @param _resolve Looks the client up per call rather than holding it. The client is replaced
     *     on every restart, and a gateway holding the old one would keep talking to a dead process.
     * @param _sink Where messages go. Optional so a caller that reports everything itself - the
     *     panels post into their own webview - is not forced to pass one.
     */
    constructor(
        private readonly _resolve: () => LspRequestSender | undefined,
        private readonly _sink?: LspMessageSink,
    ) {}

    get isRunning(): boolean {
        return this._resolve() !== undefined;
    }

    /**
     * Whether the server has finished STARTING, which is not the same as having a client object.
     *
     * `isRunning` answers "has a client been constructed", and the client is constructed several
     * lines before `start()` is called on it - so it reads true during a window in which every
     * request throws. Only the extension knows when start() resolved, so it says so.
     */
    private _ready = false;
    private readonly _waiting = new Set<(ready: boolean) => void>();

    /** The server has started and can take requests. */
    markReady(): void {
        this._ready = true;
        const waiting = [...this._waiting];
        this._waiting.clear();
        for (const release of waiting) {
            release(true);
        }
    }

    /** The server has stopped, so the next caller must wait again rather than sail past it. */
    markStopped(): void {
        this._ready = false;
    }

    /**
     * Resolves once the server can take requests, or false if it has not within `timeoutMs`.
     *
     * For the callers that run BEFORE the user does anything - a custom editor tab restored when
     * the window opens resolves long before the server is up. Without this its one request failed,
     * nothing asked again, and the tab stayed blank until the file was closed and reopened by hand.
     *
     * It gives up rather than waiting for ever, because a server that never comes is a real
     * configuration - the LSP can be switched off - and the caller already knows how to report
     * being offline.
     */
    whenReady(timeoutMs = 30000): Promise<boolean> {
        if (this._ready) {
            return Promise.resolve(true);
        }

        return new Promise<boolean>(resolve => {
            const release = (ready: boolean): void => {
                clearTimeout(timer);
                this._waiting.delete(release);
                resolve(ready);
            };

            const timer = setTimeout(() => { release(false); }, timeoutMs);
            this._waiting.add(release);
        });
    }

    /**
     * Guard for a command that must not proceed without a server.
     *
     * Returns false having said so, which is the shape the ~19 hand-written copies of this had:
     * `if (!gateway.requireRunning()) { return; }`
     */
    requireRunning(): boolean {
        if (this.isRunning) { return true; }
        this._sink?.warn(SERVER_NOT_RUNNING);
        return false;
    }

    /**
     * Sends a request. Never throws and never shows anything - the caller decides.
     *
     * This is the primitive; the helpers below are the three things callers actually do with it.
     */
    async request<T>(method: string, params: unknown = {}): Promise<LspOutcome<T>> {
        const client = this._resolve();
        if (client === undefined) {
            return { ok: false, reason: 'offline', message: SERVER_NOT_RUNNING };
        }

        try {
            return { ok: true, value: await client.sendRequest<T>(method, params) };
        } catch (e) {
            return { ok: false, reason: 'failed', message: String(e) };
        }
    }

    /**
     * Sends a request, reporting either failure to the user.
     *
     * @param context Prefixed to a failure message, so the user is told which action failed rather
     *     than only that something did. `undefined` on success is the caller's cue to stop.
     */
    async requestOrReport<T>(
        method: string, params: unknown = {}, context?: string,
    ): Promise<T | undefined> {
        const outcome = await this.request<T>(method, params);
        if (outcome.ok) { return outcome.value; }

        if (outcome.reason === 'offline') {
            this._sink?.warn(SERVER_NOT_RUNNING);
        } else {
            this._sink?.error(
                context ? `EaWEdit: ${context}: ${outcome.message}` : `EaWEdit: ${outcome.message}`);
        }
        return undefined;
    }

    /**
     * Sends a request and falls back silently.
     *
     * For the data an editor is fully usable without - stored node positions, the baseline it hides
     * inherited rows by, the completion candidates for one param slot. Turning any of those into a
     * modal would interrupt editing over a convenience, and a dropdown awaiting a reply must not
     * hang just because the answer never came.
     */
    async requestOr<T>(method: string, params: unknown, fallback: T): Promise<T> {
        const outcome = await this.request<T>(method, params);
        return outcome.ok ? outcome.value : fallback;
    }

    /**
     * Sends a request whose answer nobody reads, swallowing any failure.
     *
     * Best-effort persistence: the swimlane toggles, the saved layout. A preference that failed to
     * store is not worth a message, and there is nothing the user could do about it.
     *
     * @returns whether it got through, for a caller that wants to know.
     */
    async notify(method: string, params: unknown = {}): Promise<boolean> {
        return (await this.request(method, params)).ok;
    }

    /**
     * Runs a server-side command, reporting either failure.
     *
     * A method of its own because five call sites were spelling out the same envelope, and because
     * these commands report their own outcome: the server raises the notification saying what it
     * wrote or why it did not, so there is nothing here worth returning.
     *
     * @returns whether it ran, for a caller that follows up on success.
     */
    async executeCommand(command: string, args: unknown[] = [], context?: string): Promise<boolean> {
        const outcome = await this.request(EXECUTE_COMMAND, { command, arguments: args });
        if (outcome.ok) { return true; }

        if (outcome.reason === 'offline') {
            this._sink?.warn(SERVER_NOT_RUNNING);
        } else {
            this._sink?.error(
                context ? `EaWEdit: ${context}: ${outcome.message}` : `EaWEdit: ${outcome.message}`);
        }
        return false;
    }
}
