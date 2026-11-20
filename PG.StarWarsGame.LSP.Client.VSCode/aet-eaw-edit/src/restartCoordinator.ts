// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Restarting the servers on a configuration change or on request. Kept free of `vscode` so it can be
// unit tested; the extension supplies the stop, the start and the checks.
//
// Two rules (#395). A restart never runs beside another: one asked for meanwhile waits, and every
// one that waits is folded into a single restart after the running one, on the settings as they
// stand by then. And a configuration that cannot start a server stops none: the servers running
// stay up, the problem is shown, and the restart waits for the change that completes it.

import type { RestartReason } from "./settingsRestart";

/** What a restart is for. `command` is the Restart command; `start` is any change with no server running. */
export type RestartCause = RestartReason | "command" | "start";

/** One restart asked for: why, and what the open previews ask for again once it is done. */
export interface RestartRequest {
    causes: readonly RestartCause[];
    /** The open previews ask for their effects again. */
    effects?: boolean;
    /** The open previews read their subjects again in full: scene, models and textures. */
    assets?: boolean;
}

/** What the coordinator drives. */
export interface RestartHost {
    /** Whether any server is running. */
    running(): boolean;
    /** Whether the configuration as it stands can start a server; shows what is wrong when not. */
    validate(): boolean;
    stop(): Promise<void>;
    /** Starts the servers on the configuration as it stands. */
    start(): Promise<void>;
    log(line: string): void;
    /** Sends the open previews to read again, once the servers are back. */
    refresh(effects: boolean, assets: boolean): void;
}

/** The causes in the order their log lines are written. */
const ORDER: readonly RestartCause[] = [
    "folders",
    "features",
    "startup",
    "command",
    "start",
];

/** The log line each cause writes when it restarts a running server. LUA-02, SET-08 and SET-20 match `features`. */
const LOG_LINES: Readonly<Record<RestartCause, string | null>> = {
    folders: "Shader or game folders changed - restarting LSP server.",
    features:
        "Experimental features or executables changed - restarting LSP server.",
    startup: "Server startup settings changed - restarting LSP server.",
    command: null,
    start: null,
};

const START_LINE = "Starting LSP server after configuration change.";

/** Two requests as one: every cause once, and a preview refresh if either asked for it. */
function merge(
    a: RestartRequest | null,
    b: RestartRequest | null,
): RestartRequest | null {
    if (a === null) {
        return b;
    }
    if (b === null) {
        return a;
    }

    const causes = new Set([...a.causes, ...b.causes]);
    return {
        causes: ORDER.filter((cause) => causes.has(cause)),
        effects: (a.effects ?? false) || (b.effects ?? false),
        assets: (a.assets ?? false) || (b.assets ?? false),
    };
}

export class RestartCoordinator {
    /** Asked for and not yet run. */
    private pending: RestartRequest | null = null;
    /** Refused for a configuration that could not start a server; joins the next request. */
    private held: RestartRequest | null = null;
    /** The run under way, which every request made meanwhile joins. */
    private run: Promise<void> | null = null;

    constructor(private readonly host: RestartHost) {}

    /**
     * Asks for a restart. Settles once the servers are back - or, when one was already under way,
     * once the restart this request was folded into is done.
     */
    request(request: RestartRequest): Promise<void> {
        this.pending = merge(this.pending, request);

        if (this.run === null) {
            // Cleared in a later turn than the one that set it, even when a refused request makes
            // the drain finish without waiting on anything.
            this.run = this.drain().finally(() => {
                this.run = null;
            });
        }

        return this.run;
    }

    private async drain(): Promise<void> {
        await Promise.resolve();

        while (this.pending !== null) {
            const next = merge(this.held, this.pending)!;
            this.pending = null;

            if (!this.host.validate()) {
                // The servers keep running on the configuration they started with.
                this.held = next;
                continue;
            }
            this.held = null;

            if (this.host.running()) {
                // A start asked for while none ran, and one has started since: nothing to do.
                if (next.causes.every((cause) => cause === "start")) {
                    continue;
                }

                for (const cause of next.causes) {
                    const line = LOG_LINES[cause];
                    if (line !== null) {
                        this.host.log(line);
                    }
                }
                await this.host.stop();
            } else if (!next.causes.every((cause) => cause === "command")) {
                this.host.log(START_LINE);
            }

            await this.host.start();
            this.host.refresh(next.effects ?? false, next.assets ?? false);
        }
    }
}
