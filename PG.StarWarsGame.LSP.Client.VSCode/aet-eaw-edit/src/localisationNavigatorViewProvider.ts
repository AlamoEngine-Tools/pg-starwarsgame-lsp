// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import * as vscode from 'vscode';

import { emptyNavigatorMessage } from './navigatorPlaceholder';
import { LanguageClient } from 'vscode-languageclient/node';

import {
    CREDITS_CATEGORY, groupProjects, LocProjectInfo, LocTreeNode,
} from './webview/localisationTreeModel';

interface GetLocalisationProjectsResult { projects: LocProjectInfo[]; error?: string | null; }

type LocNodeKind = 'group' | 'layer' | 'fileset' | 'file' | 'info';

export class LocTreeItem extends vscode.TreeItem {
    constructor(
        label: string,
        collapsibleState: vscode.TreeItemCollapsibleState,
        public readonly kind: LocNodeKind,
        public readonly node?: LocTreeNode,
        public readonly project?: LocProjectInfo
    ) {
        super(label, collapsibleState);
    }
}

/**
 * Localisation Files navigator: "Text files" and "Credits files", with a project level where more
 * than one project contributes. Fed by `aet/getLocalisationProjects`.
 *
 * The grouping rules live in `webview/localisationTreeModel` so they can be tested without VS Code;
 * this class only maps the resulting nodes onto TreeItems. Like the story navigator, the root
 * re-fetches on expand, so `refresh()` after `aet/localisationIndexUpdated` is enough to stay
 * current.
 */
export class LocalisationNavigatorViewProvider implements vscode.TreeDataProvider<LocTreeItem> {
    public static readonly viewId = 'aet-eaw-edit.lsp.localisationNavigator';

    private readonly _onDidChangeTreeData = new vscode.EventEmitter<LocTreeItem | undefined>();
    readonly onDidChangeTreeData = this._onDidChangeTreeData.event;

    /**
     * Whether the workspace scan has finished.
     *
     * Until it has, an empty answer from the server means "not indexed yet", not "this workspace
     * has none" - and the tree said the latter, then corrected itself a moment later when the index
     * landed. It now says it is still working.
     */
    private _scanned = false;
    /**
     * The last answer from the server, fetched ahead of the view being opened.
     *
     * A tree view only asks for its children when it is first revealed, so without this the first
     * click paid for the round trip. Cleared by {@link refresh} so the next read is fresh.
     */
    private _cached: LocProjectInfo[] | undefined;

    constructor(private readonly _getLspClient: () => LanguageClient | undefined) {}

    refresh(): void {
        this._cached = undefined;
        this._onDidChangeTreeData.fire(undefined);
    }

    /**
     * Fetches the file list in the background and repaints.
     *
     * Called when the workspace scan completes, so the view is ready before it is looked at.
     */
    async preload(): Promise<void> {
        this._scanned = true;

        const client = this._getLspClient();
        if (!client) { return; }

        try {
            const result = await client.sendRequest<GetLocalisationProjectsResult>(
                'aet/getLocalisationProjects', {});
            this._cached = result.error ? undefined : result.projects ?? [];
        } catch {
            // Left uncached; the view falls back to fetching when it is opened.
            this._cached = undefined;
        }

        this._onDidChangeTreeData.fire(undefined);
    }

    getTreeItem(element: LocTreeItem): vscode.TreeItem {
        return element;
    }

    async getChildren(element?: LocTreeItem): Promise<LocTreeItem[]> {
        if (!element) { return this._loadRoot(); }
        return (element.node?.children ?? []).map(child => this._toItem(child));
    }

    private async _loadRoot(): Promise<LocTreeItem[]> {
        if (this._cached !== undefined) {
            return this._cached.length === 0
                ? [infoItem(emptyNavigatorMessage(this._scanned, 'localisation files'))]
                : groupProjects(this._cached).map(node => this._toItem(node));
        }

        const client = this._getLspClient();
        if (!client) { return [infoItem('LSP server is not running.')]; }

        let result: GetLocalisationProjectsResult;
        try {
            result = await client.sendRequest<GetLocalisationProjectsResult>(
                'aet/getLocalisationProjects', {});
        } catch {
            return [infoItem('Could not read localisation files from the server.')];
        }

        if (result.error) { return [infoItem(result.error)]; }
        if (!result.projects || result.projects.length === 0) {
            return [infoItem(emptyNavigatorMessage(this._scanned, 'localisation files'))];
        }

        this._cached = result.projects;
        return groupProjects(result.projects).map(node => this._toItem(node));
    }

    private _toItem(node: LocTreeNode): LocTreeItem {
        if (node.kind === 'file') { return fileItem(node); }

        const item = new LocTreeItem(
            node.label, vscode.TreeItemCollapsibleState.Expanded, node.kind, node);

        // A set of language siblings: one logical file the format forced across several. Named for
        // the file it would be if the format could hold every language, described by the ones it
        // actually covers - which is the question a translator opens this tree to answer.
        if (node.kind === 'fileset') {
            item.iconPath = new vscode.ThemeIcon('symbol-namespace');
            item.contextValue = 'aetLocFileSet';
            item.description = node.languages.join(', ');
            item.tooltip = `${node.languages.length} languages: ${node.languages.join(', ')}`;
            return item;
        }

        if (node.kind === 'group') {
            // list-ordered for credits: order is the thing that distinguishes them.
            item.iconPath = new vscode.ThemeIcon(
                node.category === CREDITS_CATEGORY ? 'list-ordered' : 'symbol-string');
            item.contextValue = 'aetLocGroup';
            item.description = `${countFiles(node)} files`;
        } else {
            item.iconPath = new vscode.ThemeIcon('folder-library');
            item.contextValue = 'aetLocLayer';
        }

        return item;
    }
}

function fileItem(node: LocTreeNode & { kind: 'file' }): LocTreeItem {
    const item = new LocTreeItem(
        node.label, vscode.TreeItemCollapsibleState.None, 'file', node, node.project);

    item.iconPath = new vscode.ThemeIcon('table');
    // Inside a set the label is the language, so the format is the same on every row and the file
    // name is the useful thing to show. The model signals that by labelling the node with something
    // other than the file's own name.
    item.description = node.label === node.project.label
        ? node.project.resourceType.toUpperCase()
        : node.project.label;
    item.tooltip = node.project.filePath;
    // Distinct context values so a menu can offer credits-only actions without checking the label.
    item.contextValue = node.project.category === CREDITS_CATEGORY ? 'aetLocFileCredits' : 'aetLocFile';
    item.command = {
        command: 'aet-eaw-edit.lsp.openLocalisationEditor',
        title: 'Open Localisation Editor',
        arguments: [item],
    };

    return item;
}

function countFiles(node: LocTreeNode): number {
    if (node.kind === 'file') { return 1; }
    return node.children.reduce((total, child) => total + countFiles(child), 0);
}

/** A plain row for the empty, error and server-down states, so the view is never blank. */
function infoItem(message: string): LocTreeItem {
    const item = new LocTreeItem(message, vscode.TreeItemCollapsibleState.None, 'info');
    item.iconPath = new vscode.ThemeIcon('info');
    return item;
}
