// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// A navigator over every open project (#230).
//
// Each project's server knows only its own campaigns and localisation files, so a navigator keeps one
// tree per project, each fed by that project's server, and puts a project level above them when the
// window holds more than one. With a single project there is no extra level: the view is exactly the
// tree it always was.

import * as vscode from "vscode";

/** What a navigator needs of one project. */
export interface NavigatorProject {
    key: string;
    name: string;
}

/** The part of a project's tree this wraps. */
export interface ProjectTree<TItem extends vscode.TreeItem> {
    readonly onDidChangeTreeData: vscode.Event<TItem | undefined>;
    getChildren(element?: TItem): Promise<TItem[]>;
    getTreeItem(element: TItem): vscode.TreeItem;
    preload(): Promise<void>;
    refresh(): void;
}

/** The row naming a project, above its tree, when the window holds several. */
export class ProjectGroupItem extends vscode.TreeItem {
    constructor(public readonly project: NavigatorProject) {
        super(project.name, vscode.TreeItemCollapsibleState.Expanded);
        this.contextValue = "project";
        this.iconPath = new vscode.ThemeIcon("project");
    }
}

export class ProjectGroupedTree<
    TItem extends vscode.TreeItem,
> implements vscode.TreeDataProvider<TItem | ProjectGroupItem> {
    private readonly _trees = new Map<string, ProjectTree<TItem>>();
    private readonly _subscriptions = new Map<string, vscode.Disposable>();
    /** Which project's tree produced an item, so a command on it reaches the right server. */
    private readonly _owners = new WeakMap<TItem, string>();
    private readonly _onDidChangeTreeData = new vscode.EventEmitter<
        TItem | ProjectGroupItem | undefined
    >();
    readonly onDidChangeTreeData = this._onDidChangeTreeData.event;

    /**
     * @param _projects The open projects, in discovery order.
     * @param _create Builds the tree of one project; called once per project key.
     */
    constructor(
        private readonly _projects: () => readonly NavigatorProject[],
        private readonly _create: (key: string) => ProjectTree<TItem>,
    ) {}

    /** The project whose tree an item came from. */
    projectOf(item: TItem): string | undefined {
        return this._owners.get(item);
    }

    /** Re-reads every project's tree, or one project's. */
    refresh(projectKey?: string): void {
        for (const [key, tree] of this._trees) {
            if (projectKey === undefined || key === projectKey) {
                tree.refresh();
            }
        }
        this._onDidChangeTreeData.fire(undefined);
    }

    async preload(projectKey?: string): Promise<void> {
        await Promise.all(
            this._projects()
                .filter((p) => projectKey === undefined || p.key === projectKey)
                .map((p) => this._tree(p.key).preload()),
        );
    }

    /** The set of projects changed: drop the trees of the ones that went and repaint. */
    projectsChanged(): void {
        const keys = new Set(this._projects().map((p) => p.key));
        for (const key of [...this._trees.keys()]) {
            if (!keys.has(key)) {
                this._trees.delete(key);
                this._subscriptions.get(key)?.dispose();
                this._subscriptions.delete(key);
            }
        }
        this._onDidChangeTreeData.fire(undefined);
    }

    getTreeItem(element: TItem | ProjectGroupItem): vscode.TreeItem {
        if (element instanceof ProjectGroupItem) {
            return element;
        }
        const key = this._owners.get(element);
        return key ? this._tree(key).getTreeItem(element) : element;
    }

    async getChildren(
        element?: TItem | ProjectGroupItem,
    ): Promise<(TItem | ProjectGroupItem)[]> {
        const projects = this._projects();
        if (element === undefined) {
            if (projects.length > 1) {
                return projects.map((p) => new ProjectGroupItem(p));
            }
            return projects.length === 1
                ? this._childrenIn(projects[0].key)
                : [];
        }
        if (element instanceof ProjectGroupItem) {
            return this._childrenIn(element.project.key);
        }
        const key = this._owners.get(element);
        return key ? this._childrenIn(key, element) : [];
    }

    private async _childrenIn(key: string, element?: TItem): Promise<TItem[]> {
        const items = await this._tree(key).getChildren(element);
        for (const item of items) {
            this._owners.set(item, key);
        }
        return items;
    }

    private _tree(key: string): ProjectTree<TItem> {
        let tree = this._trees.get(key);
        if (tree === undefined) {
            tree = this._create(key);
            this._trees.set(key, tree);
            // A project's own repaint redraws the whole view: its rows sit under a group row the
            // inner tree knows nothing about.
            this._subscriptions.set(
                key,
                tree.onDidChangeTreeData(() =>
                    this._onDidChangeTreeData.fire(undefined),
                ),
            );
        }
        return tree;
    }
}
