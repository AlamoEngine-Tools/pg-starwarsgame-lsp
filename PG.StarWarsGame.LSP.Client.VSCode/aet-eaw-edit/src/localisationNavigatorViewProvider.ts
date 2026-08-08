// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import * as vscode from 'vscode';

import { LspGateway } from './lsp/lspGateway';
import { LspTreeDataProvider } from './lspTreeDataProvider';
import { GetLocalisationProjectsResult, LocProjectInfo } from './protocol';
import { CREDITS_CATEGORY, groupProjects, LocTreeNode } from './webview/localisationTreeModel';

type LocNodeKind = 'group' | 'layer' | 'fileset' | 'file' | 'info';

export class LocTreeItem extends vscode.TreeItem {
    constructor(
        label: string,
        collapsibleState: vscode.TreeItemCollapsibleState,
        public readonly kind: LocNodeKind,
        public readonly node?: LocTreeNode,
        public readonly project?: LocProjectInfo,
        /**
         * Every file the editor should open together, for a node that stands for a set of
         * single-language files. One entry for an ordinary file.
         */
        public readonly setFilePaths?: string[],
        /** The language to show alone at first, when this node is one language of a set. */
        public readonly focusLanguage?: string,
        /**
         * What to call the tab: the set's name, not the one file the node happens to stand for.
         * Opening ENGLISH of a set is a view of the set, so the tab is named after the set.
         */
        public readonly setLabel?: string
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
export class LocalisationNavigatorViewProvider
    extends LspTreeDataProvider<LocTreeItem, GetLocalisationProjectsResult> {
    public static readonly viewId = 'aet-eaw-edit.lsp.localisationNavigator';

    protected readonly method = 'aet/getLocalisationProjects';
    protected readonly subject = 'localisation files';

    constructor(lsp: LspGateway) {
        super(lsp);
    }

    protected errorOf(data: GetLocalisationProjectsResult): string | null | undefined {
        return data.error;
    }

    protected isEmpty(data: GetLocalisationProjectsResult): boolean {
        return !data.projects || data.projects.length === 0;
    }

    protected rootItems(data: GetLocalisationProjectsResult): LocTreeItem[] {
        return groupProjects(data.projects).map(node => this._toItem(node));
    }

    protected infoItem(message: string): LocTreeItem {
        return infoItem(message);
    }

    protected childrenOf(element: LocTreeItem): LocTreeItem[] {
        // A language inside a set opens the whole set, focused on that language - the set is the
        // project, and seeing one language of it alone is a view, not a different document. The
        // context has to come from here: a child node does not know which set it belongs to.
        if (element.kind === 'fileset' && element.setFilePaths !== undefined) {
            const setFilePaths = element.setFilePaths;
            const setLabel = element.setLabel;

            return (element.node?.children ?? []).map(child => (child.kind === 'file'
                // The child's label is its language - see gatherSiblings - and the tab is named
                // after the set, because opening one language of it is a view of the set.
                ? fileItem(child, setFilePaths, child.label, setLabel)
                : this._toItem(child)));
        }

        return (element.node?.children ?? []).map(child => this._toItem(child));
    }

    private _toItem(node: LocTreeNode): LocTreeItem {
        if (node.kind === 'file') { return fileItem(node); }

        // Credits are left out of set-opening: their rows are addressed by position and duplicate
        // keys are the format, so merging them by key is not defined. They stay one file per tab.
        const setProjects = node.kind === 'fileset' && node.category !== CREDITS_CATEGORY
            ? node.children.flatMap(child => (child.kind === 'file' ? [child.project] : []))
            : [];
        const setFiles = setProjects.length > 0 ? setProjects.map(p => p.filePath) : undefined;

        // A set carries one of its own files as its project. Everything downstream reads the
        // category off that, and a set with none fell through to the "pick a file" prompt instead
        // of opening at all.
        const item = new LocTreeItem(
            node.label, vscode.TreeItemCollapsibleState.Expanded, node.kind, node, setProjects[0],
            setFiles, undefined, node.kind === 'fileset' ? node.label : undefined);

        // A set of language siblings: one logical file the format forced across several. Named for
        // the file it would be if the format could hold every language, described by the ones it
        // actually covers - which is the question a translator opens this tree to answer.
        if (node.kind === 'fileset') {
            const creditsSet = node.category === CREDITS_CATEGORY;

            item.iconPath = new vscode.ThemeIcon('symbol-namespace');
            // A credits set is a label, not a document. Its files cannot be aligned into one table
            // - credits rows are addressed by position, so there is no row N of the set - and it
            // therefore carries no project, no file paths and nothing to act on.
            //
            // Deliberately outside the `aetLocFile` prefix the tree menus match on. Sharing it gave
            // this node the whole file menu: "open editor" reached the command with no project and
            // fell through to its pick-any-file prompt, offering every localisation file in the
            // workspace including translation and .dat files, and the other three could only warn
            // that nothing was selected. Its children carry those actions, which is where a file
            // action belongs.
            item.contextValue = creditsSet ? 'aetLocCreditsSet' : 'aetLocFileSet';
            item.description = node.languages.join(', ');
            item.tooltip = creditsSet
                ? `${node.languages.length} credits files: ${node.languages.join(', ')}`
                    + ' - open one to edit it'
                : `${node.languages.length} languages: ${node.languages.join(', ')}`;

            // Opening the set is the point of grouping it: the whole project in one table, every
            // language beside its source.
            if (setFiles !== undefined && setFiles.length > 0) {
                item.command = {
                    command: 'aet-eaw-edit.lsp.openLocalisationEditor',
                    title: 'Open Localisation Editor',
                    arguments: [item],
                };
            }

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

/**
 * @param setFilePaths Every file of the set this one belongs to, when it is part of one. Opening a
 *     language opens the whole set focused on it - the set is the project, and one language of it
 *     is a view rather than a separate document.
 */
function fileItem(
    node: LocTreeNode & { kind: 'file' }, setFilePaths?: string[], focusLanguage?: string,
    setLabel?: string,
): LocTreeItem {
    const item = new LocTreeItem(
        node.label, vscode.TreeItemCollapsibleState.None, 'file', node, node.project,
        setFilePaths, focusLanguage, setLabel);

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
