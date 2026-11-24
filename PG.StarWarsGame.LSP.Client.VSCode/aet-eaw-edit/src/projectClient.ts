// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import type { ChildProcess } from "child_process";
import type * as vscode from "vscode";
import type { LanguageClient } from "vscode-languageclient/node";

import type { LspGateway } from "./lsp/lspGateway";
import type {
    ClientSpec,
    ClientState,
    LoadingPhase,
} from "./projectClientPlan";

/**
 * One language client and the server process behind it (#229).
 *
 * One runs per open project; a window with a single project runs exactly one, as it always did.
 */
export interface ProjectClient {
    spec: ClientSpec;
    client: LanguageClient | undefined;
    /**
     * The server process, while one is running.
     *
     * Held because the language client does not hold it for us on the factory path, and because a
     * shutdown that times out leaves nothing else able to reach it. Cleared by the process's own
     * `exit`, so it never names something already gone.
     */
    process: ChildProcess | undefined;
    /** Requests to this project's server; offline once it failed or stopped. */
    gateway: LspGateway;
    state: ClientState;
    /** Which feed a "loading" project waits for; cleared with every other state. */
    phase?: LoadingPhase;
    /** The line the bar shows in place of the phase for a moment, when a phase drew one. */
    gag?: string;
    /**
     * This server's watchers over directories outside the workspace folders.
     *
     * A bare glob only ever watches the folders the window is open on, so a project reached
     * through `projectReferences` was invisible: editing a dependency's XML, scripts, localisation
     * or its own `.pgproj` produced no `didChangeWatchedFiles` at all, and nothing re-indexed
     * until the server was restarted. These carry an absolute `RelativePattern` instead, and
     * their events are forwarded by hand because `synchronize.fileEvents` is fixed when the
     * client is constructed, long before the server has resolved which projects exist.
     */
    dependencyWatchers: vscode.Disposable[];
    /** Disposed with the client: a project's own trace channel, when it has one. */
    owned: vscode.Disposable[];
}
