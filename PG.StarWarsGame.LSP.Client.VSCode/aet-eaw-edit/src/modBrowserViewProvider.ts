// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The Mod Browser's outlines in the sidebar (#270): picking one opens the project's browser on it.
// The lists themselves live in the panel, where they can be paged and searched.

import * as vscode from "vscode";

import type { ProjectTree } from "./projectGroupedTree";
import type { SymbolOutline } from "./protocol/modBrowser";

export const OPEN_MOD_BROWSER = "aet-eaw-edit.lsp.openModBrowser";

/** One outline. */
export class ModBrowserOutlineItem extends vscode.TreeItem {
    constructor(
        public readonly outline: SymbolOutline,
        label: string,
        icon: string,
    ) {
        super(label, vscode.TreeItemCollapsibleState.None);
        this.contextValue = "aetModBrowserOutline";
        this.iconPath = new vscode.ThemeIcon(icon);
        this.command = {
            command: OPEN_MOD_BROWSER,
            title: vscode.l10n.t("Open in the Mod Browser"),
            arguments: [this],
        };
    }
}

/** One project's outlines. They are fixed, so there is nothing to fetch. */
export class ModBrowserViewProvider implements ProjectTree<ModBrowserOutlineItem> {
    static readonly viewId = "aet-eaw-edit.lsp.modBrowser";

    private readonly _onDidChangeTreeData = new vscode.EventEmitter<
        ModBrowserOutlineItem | undefined
    >();
    readonly onDidChangeTreeData = this._onDidChangeTreeData.event;

    getChildren(
        element?: ModBrowserOutlineItem,
    ): Promise<ModBrowserOutlineItem[]> {
        return Promise.resolve(
            element
                ? []
                : [
                      new ModBrowserOutlineItem(
                          "gameData",
                          vscode.l10n.t("Game Data"),
                          "symbol-class",
                      ),
                      new ModBrowserOutlineItem(
                          "assets",
                          vscode.l10n.t("Assets"),
                          "file-media",
                      ),
                      new ModBrowserOutlineItem(
                          "schema",
                          vscode.l10n.t("Schema"),
                          "symbol-enum",
                      ),
                  ],
        );
    }

    getTreeItem(element: ModBrowserOutlineItem): vscode.TreeItem {
        return element;
    }

    preload(): Promise<void> {
        return Promise.resolve();
    }

    refresh(): void {
        this._onDidChangeTreeData.fire(undefined);
    }
}
