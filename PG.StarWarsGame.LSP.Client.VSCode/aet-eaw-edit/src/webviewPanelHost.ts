// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What every webview panel in this extension is made of.
//
// There were three of them and each had built its own: its own `static _panels` map, its own
// case-insensitive key function (two were byte-identical, comment included), its own `disposeAll`,
// its own `_post`, its own `createWebviewPanel` options, and its own copy of the HTML skeleton with
// the same CSP and the same codicon <link>. The three skeletons had already drifted - one was
// missing the body reset the other two had - which is the sort of difference nobody notices until a
// footer is off the bottom of a window.
//
// What actually differs between the three is a view type, a title, a script name and a message
// switch. Everything above is here.

import * as vscode from 'vscode';

/** What makes one panel different from another. */
export interface WebviewPanelSpec {
    /**
     * The VS Code view type. Not cosmetic: a `menus` contribution in package.json targets a panel
     * by this, which is why the localisation editor has two of them - a credits file has a crawl to
     * preview and a text file has nothing of the sort.
     */
    readonly viewType: string;
    readonly title: string;
    readonly column: vscode.ViewColumn;
    /** Bundle to load, relative to `out/webview`. */
    readonly script: string;
    /**
     * Extra CSS for the page itself.
     *
     * Only for what the host has to establish before the bundle runs - a grid that fills the tab
     * needs `height: 100%` on html/body, and a bundle cannot set that on itself in time. The story
     * graph passes none because it resets the page from inside its own styled-components global.
     */
    readonly bodyStyle?: string;
    /** The grid has its own filter box, but Ctrl+F in a table is muscle memory. */
    readonly enableFindWidget?: boolean;
}

/**
 * A webview panel with its plumbing already done.
 *
 * Subclasses supply a spec and handle messages; everything about creating the panel, addressing its
 * webview and tearing it down lives here.
 */
export abstract class WebviewPanelHost {
    protected readonly panel: vscode.WebviewPanel;

    /**
     * The tab was closed.
     *
     * An event rather than an overridable method: the registry needs to hear about it and so does
     * the subclass, and neither should have to remember to call the other. Subscribing beats
     * `super.onDisposed()`, which is exactly the call that gets forgotten.
     */
    readonly onDidDispose: vscode.Event<void>;

    protected constructor(extensionUri: vscode.Uri, spec: WebviewPanelSpec) {
        this.panel = vscode.window.createWebviewPanel(
            spec.viewType, spec.title, spec.column,
            {
                enableScripts: true,
                retainContextWhenHidden: true,
                ...(spec.enableFindWidget ? { enableFindWidget: true } : {}),
                // Everything the webviews load lives under out/. The VSIX is packaged with
                // `vsce package --no-dependencies`, so anything addressed via node_modules 404s in
                // the published extension - it only works in dev because the folder happens to be
                // there. See copyCodicons in esbuild.js.
                localResourceRoots: [
                    vscode.Uri.joinPath(extensionUri, 'out', 'webview'),
                    vscode.Uri.joinPath(extensionUri, 'out', 'codicons'),
                ],
            });

        const scriptUri = this.panel.webview.asWebviewUri(
            vscode.Uri.joinPath(extensionUri, 'out', 'webview', spec.script));
        const codiconUri = this.panel.webview.asWebviewUri(
            vscode.Uri.joinPath(extensionUri, 'out', 'codicons', 'codicon.css'));

        this.panel.webview.html = buildHtml(
            scriptUri, codiconUri, this.panel.webview.cspSource, spec.bodyStyle);

        this.onDidDispose = this.panel.onDidDispose;
        this.panel.webview.onDidReceiveMessage(
            (msg: WebviewMessage) => void this.onMessage(msg));
    }

    /** Handles one message from the webview. */
    protected abstract onMessage(msg: WebviewMessage): void | Promise<void>;

    protected post(msg: unknown): void {
        void this.panel.webview.postMessage(msg);
    }

    reveal(preserveFocus = false): void {
        this.panel.reveal(this.panel.viewColumn, preserveFocus);
    }

    dispose(): void {
        this.panel.dispose();
    }
}

/** A message from a webview: a tagged union the subclass switches on. */
export interface WebviewMessage {
    type: string;
    [key: string]: unknown;
}

/**
 * The open panels of one kind, keyed.
 *
 * An instance held as a `static` on each panel class, rather than a base-class static: a static
 * field on a base is shared by every subclass, so all three kinds would have landed in one map and
 * `disposeAll` on any of them would have closed the lot.
 */
export class PanelRegistry<T extends WebviewPanelHost> {
    private readonly _panels = new Map<string, T>();

    get(key: string): T | undefined {
        return this._panels.get(key);
    }

    has(key: string): boolean {
        return this._panels.has(key);
    }

    get all(): T[] {
        return [...this._panels.values()];
    }

    /** Registers a panel and arranges for it to remove itself when its tab closes. */
    track(key: string, panel: T): T {
        this._panels.set(key, panel);
        panel.onDidDispose(() => this._panels.delete(key));
        return panel;
    }

    disposeAll(): void {
        // Copied first: disposing runs the hook that mutates the map.
        for (const panel of this.all) { panel.dispose(); }
    }
}

/**
 * Identifies a panel that stands for a file.
 *
 * Lower-cased because Windows hands the same file back under different casings, and two panels for
 * one file each hold their own staged queue and their own content hash - whichever saved second
 * would be refused as stale, having silently lost the other's work.
 */
export function panelKey(filePath: string): string {
    return filePath.toLowerCase();
}

/**
 * Identifies a panel that stands for a set of files.
 *
 * Sorted, so opening the same files in a different order reveals the tab that is already open
 * rather than a second one staging edits against the same files.
 */
export function panelSetKey(filePaths: string[]): string {
    return filePaths.map(panelKey).sort().join('|');
}

function buildHtml(
    scriptUri: vscode.Uri, codiconUri: vscode.Uri, cspSource: string, bodyStyle?: string,
): string {
    // A bundled script, so script-src is the extension origin rather than 'unsafe-inline' - which
    // is what the old sidebar webview needed, having its JS inlined as a template literal.
    // style-src still needs 'unsafe-inline' for styled-components' injected style tags; the codicon
    // stylesheet and its .ttf load from the extension origin (cspSource covers style-src and
    // font-src).
    return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none'; style-src 'unsafe-inline' ${cspSource}; script-src ${cspSource}; font-src ${cspSource}; img-src ${cspSource} data:;">
<link rel="stylesheet" href="${codiconUri}">${bodyStyle ? `
<style>
${bodyStyle}
</style>` : ''}
</head>
<body>
<div id="root"></div>
<script src="${scriptUri}"></script>
</body>
</html>`;
}
