// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import * as vscode from 'vscode';

import { LspMessageSink } from './lspGateway';

/**
 * The production sink: gateway messages become VS Code notifications.
 *
 * Its own module so `lspGateway` stays free of `vscode` and therefore unit-testable - the gateway
 * is where the retry and reporting rules live, and those are exactly what wants testing.
 */
export const vscodeMessageSink: LspMessageSink = {
    warn: message => { void vscode.window.showWarningMessage(message); },
    error: message => { void vscode.window.showErrorMessage(message); },
};
