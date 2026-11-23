// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import * as os from "os";
import * as vscode from "vscode";

import { homeScrubber, scrubbed } from "./homePath";

const ownHome = homeScrubber(os.homedir(), process.platform === "win32");

/**
 * `channel`, writing the home directory as `~` whatever is written to it and by whom. Every output
 * channel the extension makes goes through this: they are read on screen and pasted into public
 * issues, and a path under the home directory carries the user's name.
 */
export function withoutHome<T extends object>(channel: T): T {
    return scrubbed(channel, ownHome);
}

/**
 * The extension's "EaWEdit" output channel, shared by the activation code and the panels so a
 * session leaves one readable record. Set once by activate(), {@link withoutHome}; lines before
 * that are dropped.
 */
let channel: vscode.OutputChannel | undefined;

export function setLogChannel(next: vscode.OutputChannel | undefined): void {
    channel = next;
}

export function logLine(msg: string): void {
    const ts = new Date().toISOString().replace("T", " ").replace("Z", "");
    try {
        channel?.appendLine(`[${ts}] ${msg}`);
    } catch {
        // The host disposes the output channel before every extension's deactivate() has finished,
        // and a disposed channel THROWS on append rather than ignoring it. The lines that land
        // there are the last ones a session writes - how the server was stopped - so this fired on
        // every exit, was logged as an extension error, and could abandon the rest of the shutdown
        // from inside stopClient. There is no API to ask whether a channel is still open.
    }
}
