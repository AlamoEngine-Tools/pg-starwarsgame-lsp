// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Stopping the language server without letting a slow stop break the next start.
//
// `LanguageClient.stop()` races the shutdown handshake against a timer and THROWS when the timer
// wins. The old call site was `await lspClient.stop()` followed by the two lines that clear our own
// state, so a timeout skipped both: the module kept a reference to a client the library had already
// marked Stopped, and the restart that was supposed to follow never ran. Changing a feature flag
// while the host was busy therefore left the LSP silently dead until the window was reloaded.
//
// Measured against the real server, the handshake costs about 60ms - shutdown answered in 5-6ms,
// process gone ~55ms later - whether the workspace is idle, mid-scan or fully indexed. So a stop
// that times out is never the server taking its time; it is the extension host being too busy to
// run our continuations, which is exactly what deactivate looks like when a dozen extensions tear
// down together.
//
// Kept free of `vscode` so it can be unit-tested, like lspGateway beside it.

/** The slice of `LanguageClient` this needs - so a test can supply an object literal. */
export interface StoppableClient {
    stop(timeout?: number): Promise<void>;
}

/** Where the diagnostic line goes. The extension's output channel in production. */
export type StopLogger = (message: string) => void;

/**
 * How long to give the handshake.
 *
 * The library's own default is 2000ms. This is deliberately more generous: the cost of waiting
 * longer is a slower window close, while the cost of giving up early is an orphaned server process
 * holding the workspace open. The handshake itself needs about 60ms, so anything reached here is a
 * congested event loop rather than a slow server.
 */
export const STOP_TIMEOUT_MS = 5000;

/** What happened, for the caller that has to decide whether to start a new client. */
export type StopOutcome =
/** The handshake completed; the server is gone. */
    | 'stopped'
    /** There was nothing to stop. */
    | 'idle'
    /** The stop threw, so the server was killed instead. A restart is safe either way. */
    | 'abandoned';

export interface StopOptions {
    /** Overrides {@link STOP_TIMEOUT_MS}. */
    readonly timeoutMs?: number;

    /**
     * Terminates the server process. Called ONLY when the handshake fails.
     *
     * The library has a net of its own here - `checkProcessDied` force-terminates two seconds
     * after shutdown settles - but it is a `setTimeout`, and on deactivate the extension host
     * exits long before it fires. That is exactly the case that strands a server: the run where
     * the host is too busy to finish the handshake is the same run where it is about to stop
     * running timers at all. So the kill has to happen in line, while there is still a process
     * alive to perform it.
     *
     * Not called after a clean stop. The server exits by itself about 55ms later, and killing it
     * mid-exit would cut short the log flush that makes the next crash diagnosable.
     */
    readonly forceKill?: () => void;
}

/**
 * Stops <paramref name="client" />, and reports rather than throws when it will not stop.
 *
 * Never rejects. Every caller either restarts the server afterwards or is deactivate itself, and
 * neither has anything useful to do with an exception: the client is marked Stopped by the library
 * even on the timeout path, so the only question left is whether we are free to start a new one.
 * We always are.
 */
export async function stopClient(
    client: StoppableClient | undefined,
    log: StopLogger,
    options: StopOptions = {},
): Promise<StopOutcome> {
    if (client === undefined) {
        return 'idle';
    }

    log('Stopping LSP server.');
    try {
        await client.stop(options.timeoutMs ?? STOP_TIMEOUT_MS);
        return 'stopped';
    } catch (err) {
        // The library clears its own connection in a `finally`, so it cannot be asked to try
        // again, and `dispose()` would only report that the client is no longer running. Killing
        // the process is the only cleanup left that still works from here.
        log(`Stopping the LSP server failed: ${describe(err)}. Killing it.`);
        try {
            options.forceKill?.();
        } catch (killErr) {
            // Almost always the process having already gone (ESRCH), which is the outcome we
            // wanted. Worth a line either way: a kill that fails for some OTHER reason is how a
            // server survives its window, and that is the thing nobody notices until the next
            // start behaves strangely.
            log(`Killing the LSP server failed: ${describe(killErr)}.`);
        }
        return 'abandoned';
    }
}

function describe(err: unknown): string {
    if (err instanceof Error) {
        return err.message;
    }
    return String(err);
}
