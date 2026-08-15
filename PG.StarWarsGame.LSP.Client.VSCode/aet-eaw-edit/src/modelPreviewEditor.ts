// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Opening a .alo or .ala shows the model rather than binary garbage.
//
// A read-only CUSTOM editor rather than a command, because the file is the thing the user opened -
// double-clicking a model in the explorer, or following a go-to-definition onto one, should land on
// the model. A command would leave the default binary editor as what happens by default, which is
// the behaviour this replaces.

import * as vscode from 'vscode';

import { LspGateway } from './lsp/lspGateway';
import { ModelPreviewPanel, PreviewSubject } from './modelPreviewPanel';

/**
 * A document with no content of its own.
 *
 * The bytes are never read here: the server resolves the file through the same layered view of the
 * game the rest of the extension uses, so reading them in the client would be a second, different
 * answer to "which model is this".
 */
class PreviewDocument implements vscode.CustomDocument {
    constructor(readonly uri: vscode.Uri) { }

    dispose(): void {
        // Nothing held.
    }
}

export class ModelPreviewEditorProvider implements vscode.CustomReadonlyEditorProvider {
    /** Matches the `customEditors` contribution in package.json. */
    static readonly viewType = 'aet-eaw-edit.modelPreview';

    constructor(
        private readonly extensionUri: vscode.Uri,
        private readonly lsp: LspGateway,
    ) { }

    static register(
        context: vscode.ExtensionContext, lsp: LspGateway,
    ): vscode.Disposable {
        return vscode.window.registerCustomEditorProvider(
            ModelPreviewEditorProvider.viewType,
            new ModelPreviewEditorProvider(context.extensionUri, lsp),
            {
                // A GPU context and several megabytes of geometry are expensive to rebuild, and a
                // model tab is something people switch away from and back to constantly.
                webviewOptions: { retainContextWhenHidden: true },
                // Two tabs on one model would mean two GPU contexts for the same picture.
                supportsMultipleEditorsPerDocument: false,
            });
    }

    openCustomDocument(uri: vscode.Uri): vscode.CustomDocument {
        return new PreviewDocument(uri);
    }

    resolveCustomEditor(
        document: vscode.CustomDocument, panel: vscode.WebviewPanel,
    ): void {
        ModelPreviewPanel.adopt(
            this.extensionUri, this.lsp, subjectFor(document.uri),
            basename(document.uri), panel);
    }
}

/**
 * What the preview should show for a file.
 *
 * An `.ala` names no model, so the server has to recover the pairing from the filename and then
 * verify it against the skeleton - a shipped animation can sit right beside a model it does not
 * belong to. That work is the server's; the client only says which kind of file this is.
 */
function subjectFor(uri: vscode.Uri): PreviewSubject {
    const reference = uri.toString();

    return uri.path.toLowerCase().endsWith('.ala')
        ? { kind: 'animation', animationReference: reference }
        : { kind: 'model', modelReference: reference };
}

function basename(uri: vscode.Uri): string {
    const path = uri.path;
    const slash = path.lastIndexOf('/');
    return slash >= 0 ? path.slice(slash + 1) : path;
}
