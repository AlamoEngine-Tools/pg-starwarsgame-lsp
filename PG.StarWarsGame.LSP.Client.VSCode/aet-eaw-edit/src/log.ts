// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import * as vscode from 'vscode';

/**
 * The extension's "EaWEdit" output channel, shared by the activation code and the panels so a
 * session leaves one readable record. Set once by activate(); lines before that are dropped.
 */
let channel: vscode.OutputChannel | undefined;

export function setLogChannel(next: vscode.OutputChannel | undefined): void {
    channel = next;
}

export function logLine(msg: string): void {
    const ts = new Date().toISOString().replace('T', ' ').replace('Z', '');
    channel?.appendLine(`[${ts}] ${msg}`);
}
