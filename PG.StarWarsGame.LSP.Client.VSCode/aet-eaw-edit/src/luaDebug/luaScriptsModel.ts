// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The scripts view without VS Code: the adapter's eawLua/* payloads and the rows they become.
// Pure so it runs under the node test harness; luaScriptsViewProvider.ts turns rows into
// TreeItems and talks to the debug session.

/** The custom requests and event the adapter serves for this view; names shared with the C# side. */
export const LUA_SCRIPTS_REQUEST = 'eawLua/scripts';
export const LUA_REFRESH_REQUEST = 'eawLua/refresh';
export const LUA_SELECT_SCRIPT_REQUEST = 'eawLua/selectScript';
export const LUA_BREAK_THREAD_REQUEST = 'eawLua/breakThread';
export const LUA_SCRIPTS_CHANGED_EVENT = 'eawLua/scriptsChanged';

export type LuaRunState = 'running' | 'breakArmed' | 'suspended';

export interface LuaThreadRow {
    threadIndex: number;
    name: string;
}

/** One live script instance as the adapter reports it. */
export interface LuaScriptRow {
    scriptId: number;
    gamePath: string;
    name: string;
    /** The workspace file the game path resolves to, or null when it is under no source root. */
    path: string | null;
    attached: boolean;
    isContext: boolean;
    isSuspended: boolean;
    /** Null until the game has been asked for this script's threads. */
    threads: LuaThreadRow[] | null;
}

export interface LuaScriptsResult {
    runState: LuaRunState;
    scripts: LuaScriptRow[];
}

export interface LuaScriptsChangedEvent {
    reason: 'scriptAdded' | 'scriptRemoved' | 'suspended' | 'stateChanged';
    scriptId: number | null;
}

/** A script row of the tree, presentation decided. */
export interface ScriptNode {
    kind: 'script';
    scriptId: number;
    label: string;
    description: string;
    tooltip: string;
    path: string | null;
    icon: string;
    contextValue: 'aetLuaScript';
    threads: LuaThreadRow[] | null;
}

/** A thread row under a script; index -1 is the script's main state, which every script has. */
export interface ThreadNode {
    kind: 'thread';
    scriptId: number;
    threadIndex: number;
    label: string;
    description: string | undefined;
    icon: string;
    contextValue: 'aetLuaThread';
}

export function scriptRows(result: LuaScriptsResult): ScriptNode[] {
    return result.scripts.map(script => {
        const state = script.isSuspended ? 'stopped' : script.isContext ? 'context' : script.attached ? 'attached' : null;
        const location = script.path === null
            ? `${script.gamePath}\nThis file is not under any source root, so breakpoints cannot be mapped to it`
            : script.path;
        return {
            kind: 'script',
            scriptId: script.scriptId,
            label: script.name,
            description: state === null ? `#${script.scriptId}` : `#${script.scriptId} - ${state}`,
            tooltip: `${script.name} [${script.scriptId}]\n${location}`,
            path: script.path,
            icon: script.isSuspended ? 'debug-pause' : script.attached || script.isContext ? 'debug-breakpoint-log' : 'file-code',
            contextValue: 'aetLuaScript',
            threads: script.threads,
        };
    });
}

/**
 * The rows under a script, or undefined when its threads have not been fetched yet - the view
 * then asks the adapter for them on expand rather than showing an empty node.
 */
export function threadRows(script: ScriptNode): ThreadNode[] | undefined {
    if (script.threads === null) {
        return undefined;
    }
    const main: ThreadNode = {
        kind: 'thread',
        scriptId: script.scriptId,
        threadIndex: -1,
        label: 'main state',
        description: undefined,
        icon: 'symbol-event',
        contextValue: 'aetLuaThread',
    };
    return [main, ...script.threads.map<ThreadNode>(thread => ({
        kind: 'thread',
        scriptId: script.scriptId,
        threadIndex: thread.threadIndex,
        label: thread.name,
        description: `#${thread.threadIndex}`,
        icon: 'symbol-event',
        contextValue: 'aetLuaThread',
    }))];
}

/**
 * Whether the game would accept a break right now, with the engine's rule when it would not.
 * The engine asserts on a break while a script is stopped or while one is already armed, so
 * the adapter refuses both and the view says why before the click.
 */
export function breakAvailability(runState: LuaRunState): { allowed: true } | { allowed: false; reason: string } {
    switch (runState) {
        case 'running':
            return {allowed: true};
        case 'breakArmed':
            return {allowed: false, reason: 'A break is already armed and waiting for the next Lua line'};
        case 'suspended':
            return {allowed: false, reason: 'A script is stopped; continue or step first'};
    }
}

/** The one-line state for the view's description: how many instances, and what the game is doing. */
export function sessionSummary(result: LuaScriptsResult): string {
    const count = result.scripts.length === 1 ? '1 script' : `${result.scripts.length} scripts`;
    if (result.runState === 'suspended') {
        const stopped = result.scripts.find(s => s.isSuspended);
        return stopped ? `${count} - stopped in ${stopped.name} [${stopped.scriptId}]` : `${count} - stopped`;
    }
    return `${count} - ${result.runState === 'breakArmed' ? 'break armed' : 'running'}`;
}
