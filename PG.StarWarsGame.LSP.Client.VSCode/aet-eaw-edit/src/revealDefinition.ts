// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Opening the XML that defines a value a panel holds by name.
//
// Both the story graph and the model preview need this and neither has a document position to
// offer: a graph param slot and an ability row are both just a name plus the type it should be.
// The story editor had it first, behind its feature flag; the lookup itself was never
// story-specific, so it now sits on aet/resolveReference and this is the one caller-side copy.

import * as vscode from 'vscode';

import { LspGateway } from './lsp/lspGateway';
import { ResolveReferenceResult } from './protocol/workspace';

/**
 * Resolves `value` and opens its definition beside the panel.
 *
 * Failures are reported to the reader rather than swallowed: a value that does not resolve and one
 * that lives in the base game look identical from the panel, and only the first is theirs to fix.
 *
 * @param method The request to use. Defaults to the ungated endpoint; the story graph passes its
 *     own so the lookup stays behind that feature's flag where the feature owns it.
 */
export async function revealDefinition(
    lsp: LspGateway,
    value: string,
    referenceType?: string,
    method = 'aet/resolveReference',
): Promise<void> {
    if (!value) { return; }

    const result = await lsp.requestOrReport<ResolveReferenceResult>(
        method, { value, referenceType }, `cannot open the definition of '${value}'`);
    if (result === undefined) { return; }

    if (result.error || !result.uri) {
        void vscode.window.showWarningMessage(
            `EaWEdit: ${result.error ?? `Cannot resolve '${value}'.`}`);
        return;
    }

    try {
        const doc = await vscode.workspace.openTextDocument(vscode.Uri.parse(result.uri));
        const position = new vscode.Position(Math.max(0, result.line), Math.max(0, result.column));
        await vscode.window.showTextDocument(doc, {
            viewColumn: vscode.ViewColumn.Beside,
            selection: new vscode.Range(position, position),
        });
    } catch (e) {
        void vscode.window.showErrorMessage(`EaWEdit: Cannot open ${result.uri} - ${e}`);
    }
}
