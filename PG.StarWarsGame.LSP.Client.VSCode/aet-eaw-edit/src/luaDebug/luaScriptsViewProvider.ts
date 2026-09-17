// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import * as vscode from 'vscode';

import {LUA_DEBUG_TYPE} from './luaDebugAdapterFactory';
import {
    breakAvailability, LUA_BREAK_THREAD_REQUEST, LUA_REFRESH_REQUEST, LUA_SCRIPTS_CHANGED_EVENT, LUA_SCRIPTS_REQUEST,
    LUA_SELECT_SCRIPT_REQUEST, type LuaScriptsResult, type ScriptNode, scriptRows, sessionSummary, type ThreadNode,
    threadRows,
} from './luaScriptsModel';

/** Context keys the manifest's enablement clauses read, so a refused action is greyed out, not hidden. */
export const LUA_SESSION_ACTIVE_KEY = 'aet-eaw-edit.luaDebug.sessionActive';
export const LUA_CAN_BREAK_KEY = 'aet-eaw-edit.luaDebug.canBreak';

type InfoNode = { kind: 'info'; label: string };
type Node = ScriptNode | ThreadNode | InfoNode;

export class LuaScriptsTreeItem extends vscode.TreeItem {
    constructor(public readonly node: Node, collapsibleState: vscode.TreeItemCollapsibleState) {
        super(node.label, collapsibleState);
    }
}

/**
 * The Lua scripts view in Run and Debug: every script instance the game reports, its named
 * coroutine threads under it, and the actions the engine offers - break in a script, break in
 * one of its threads, refresh. The game cannot start a script from the debugger, so this list is
 * the way in: pick the instance, arm a break, and the stop lands in the normal call stack view.
 *
 * Fed by the active eaw-lua session's custom requests; the adapter's scriptsChanged event
 * (one per added or removed instance, which is hundreds while a map loads) is coalesced into
 * one refresh.
 */
export class LuaScriptsViewProvider implements vscode.TreeDataProvider<LuaScriptsTreeItem>, vscode.Disposable {
    public static readonly viewId = 'aet-eaw-edit.luaDebug.scripts';

    private readonly _onDidChangeTreeData = new vscode.EventEmitter<LuaScriptsTreeItem | undefined>();
    readonly onDidChangeTreeData = this._onDidChangeTreeData.event;

    private readonly _view: vscode.TreeView<LuaScriptsTreeItem>;
    private readonly _subscriptions: vscode.Disposable[] = [];
    private _session: vscode.DebugSession | undefined;
    private _result: LuaScriptsResult | undefined;
    private _refreshTimer: ReturnType<typeof setTimeout> | undefined;

    constructor() {
        this._view = vscode.window.createTreeView(LuaScriptsViewProvider.viewId, {
            treeDataProvider: this,
            showCollapseAll: true,
        });
        this._subscriptions.push(
            this._view,
            vscode.debug.onDidChangeActiveDebugSession(session => this._track(session)),
            vscode.debug.onDidTerminateDebugSession(session => {
                if (session === this._session) {
                    this._track(undefined);
                }
            }),
            vscode.debug.onDidReceiveDebugSessionCustomEvent(event => {
                if (event.session === this._session && event.event === LUA_SCRIPTS_CHANGED_EVENT) {
                    this._scheduleRefresh();
                }
            }),
        );
        this._track(vscode.debug.activeDebugSession);
    }

    dispose(): void {
        if (this._refreshTimer !== undefined) {
            clearTimeout(this._refreshTimer);
        }
        for (const subscription of this._subscriptions) {
            subscription.dispose();
        }
        this._onDidChangeTreeData.dispose();
    }

    // -- actions ----------------------------------------------------------------------------

    /** Reloads the script list from the game. */
    async refresh(): Promise<void> {
        await this._act(LUA_REFRESH_REQUEST, {});
    }

    /** Reloads one script's threads from the game. */
    async refreshThreads(item: LuaScriptsTreeItem | undefined): Promise<void> {
        if (item?.node.kind === 'script') {
            await this._act(LUA_REFRESH_REQUEST, {scriptId: item.node.scriptId});
        }
    }

    /** Arms a break at the next Lua line of the script; the stop shows up in the call stack view. */
    async breakScript(item: LuaScriptsTreeItem | undefined): Promise<void> {
        if (item?.node.kind === 'script' && this._breakAllowed()) {
            await this._act(LUA_SELECT_SCRIPT_REQUEST, {scriptId: item.node.scriptId});
        }
    }

    /** Arms a break at the next Lua line of one coroutine thread of a script. */
    async breakThread(item: LuaScriptsTreeItem | undefined): Promise<void> {
        if (item?.node.kind === 'thread' && this._breakAllowed()) {
            await this._act(LUA_BREAK_THREAD_REQUEST, {
                scriptId: item.node.scriptId,
                threadIndex: item.node.threadIndex,
            });
        }
    }

    // -- the tree ---------------------------------------------------------------------------

    getTreeItem(element: LuaScriptsTreeItem): vscode.TreeItem {
        return element;
    }

    async getChildren(element?: LuaScriptsTreeItem): Promise<LuaScriptsTreeItem[]> {
        if (element === undefined) {
            return this._roots();
        }
        if (element.node.kind !== 'script') {
            return [];
        }
        return this._threadsOf(element.node);
    }

    private async _roots(): Promise<LuaScriptsTreeItem[]> {
        if (this._session === undefined) {
            return [this._info('No Lua debug session. Start one from Run and Debug (Attach to Empire at War)')];
        }
        if (this._result === undefined) {
            const outcome = await this._request<LuaScriptsResult>(LUA_SCRIPTS_REQUEST, {});
            if (outcome === undefined) {
                return [this._info('The debug adapter did not answer; use Refresh to try again')];
            }
            this._apply(outcome);
        }
        const rows = scriptRows(this._result!);
        if (rows.length === 0) {
            return [this._info('The game reports no running scripts')];
        }
        return rows.map(row => this._scriptItem(row));
    }

    private async _threadsOf(script: ScriptNode): Promise<LuaScriptsTreeItem[]> {
        let rows = threadRows(script);
        if (rows === undefined) {
            // First expand: ask the game for this script's threads, then read them off the new list.
            const outcome = await this._request<LuaScriptsResult>(LUA_REFRESH_REQUEST, {scriptId: script.scriptId});
            if (outcome === undefined) {
                return [this._info('The game did not list this script\'s threads')];
            }
            this._apply(outcome, false);
            const fresh = scriptRows(outcome).find(row => row.scriptId === script.scriptId);
            rows = fresh === undefined ? [] : threadRows(fresh) ?? [];
        }
        return rows.map(row => this._threadItem(row));
    }

    private _scriptItem(row: ScriptNode): LuaScriptsTreeItem {
        const item = new LuaScriptsTreeItem(row, vscode.TreeItemCollapsibleState.Collapsed);
        item.id = `script:${row.scriptId}`;
        item.description = row.description;
        item.tooltip = this._withBreakRule(row.tooltip);
        item.iconPath = new vscode.ThemeIcon(row.icon);
        item.contextValue = row.contextValue;
        if (row.path !== null) {
            item.resourceUri = vscode.Uri.file(row.path);
            item.command = {
                command: 'vscode.open',
                title: 'Open Script',
                arguments: [vscode.Uri.file(row.path)],
            };
        }
        return item;
    }

    private _threadItem(row: ThreadNode): LuaScriptsTreeItem {
        const item = new LuaScriptsTreeItem(row, vscode.TreeItemCollapsibleState.None);
        item.id = `thread:${row.scriptId}:${row.threadIndex}`;
        item.description = row.description;
        item.tooltip = this._withBreakRule(`${row.label} of script ${row.scriptId}`);
        item.iconPath = new vscode.ThemeIcon(row.icon);
        item.contextValue = row.contextValue;
        return item;
    }

    private _info(message: string): LuaScriptsTreeItem {
        const item = new LuaScriptsTreeItem({kind: 'info', label: message}, vscode.TreeItemCollapsibleState.None);
        item.iconPath = new vscode.ThemeIcon('info');
        item.contextValue = 'aetLuaInfo';
        return item;
    }

    /** The row's tooltip, plus why a break is refused right now so the greyed icon explains itself. */
    private _withBreakRule(tooltip: string): string {
        if (this._result === undefined) {
            return tooltip;
        }
        const availability = breakAvailability(this._result.runState);
        return availability.allowed ? tooltip : `${tooltip}\nBreak unavailable - ${availability.reason}`;
    }

    // -- session plumbing -------------------------------------------------------------------

    private _track(session: vscode.DebugSession | undefined): void {
        const next = session?.type === LUA_DEBUG_TYPE ? session : undefined;
        if (next === this._session) {
            return;
        }
        this._session = next;
        this._result = undefined;
        this._view.description = undefined;
        void vscode.commands.executeCommand('setContext', LUA_SESSION_ACTIVE_KEY, next !== undefined);
        void vscode.commands.executeCommand('setContext', LUA_CAN_BREAK_KEY, false);
        this._onDidChangeTreeData.fire(undefined);
    }

    private _scheduleRefresh(): void {
        if (this._refreshTimer !== undefined) {
            clearTimeout(this._refreshTimer);
        }
        this._refreshTimer = setTimeout(() => {
            this._refreshTimer = undefined;
            this._result = undefined;
            this._onDidChangeTreeData.fire(undefined);
        }, 150);
    }

    private _breakAllowed(): boolean {
        if (this._result === undefined) {
            return true;
        }
        const availability = breakAvailability(this._result.runState);
        if (!availability.allowed) {
            void vscode.window.showWarningMessage(availability.reason);
        }
        return availability.allowed;
    }

    /** Sends an action whose answer is the new list; a refusal is shown with the adapter's reason. */
    private async _act(request: string, args: object): Promise<void> {
        const outcome = await this._request<LuaScriptsResult>(request, args);
        if (outcome !== undefined) {
            this._apply(outcome);
        }
    }

    private async _request<T>(request: string, args: object): Promise<T | undefined> {
        const session = this._session;
        if (session === undefined) {
            void vscode.window.showWarningMessage('No Lua debug session is active');
            return undefined;
        }
        try {
            return await session.customRequest(request, args) as T;
        } catch (error) {
            void vscode.window.showErrorMessage(error instanceof Error ? error.message : String(error));
            return undefined;
        }
    }

    private _apply(result: LuaScriptsResult, repaint = true): void {
        this._result = result;
        this._view.description = sessionSummary(result);
        void vscode.commands.executeCommand('setContext', LUA_CAN_BREAK_KEY, breakAvailability(result.runState).allowed);
        if (repaint) {
            this._onDidChangeTreeData.fire(undefined);
        }
    }
}
