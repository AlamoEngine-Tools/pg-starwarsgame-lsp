// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The Lua debugger: its debug type, its adapter and the scripts view's commands.

import * as vscode from "vscode";

import { logLine } from "../log";
import {
    LUA_DEBUG_TYPE,
    LuaDebugAdapterDescriptorFactory,
} from "../luaDebug/luaDebugAdapterFactory";
import { LuaDebugConfigurationProvider } from "../luaDebug/luaDebugConfigurationProvider";
import {
    LuaScriptsTreeItem,
    LuaScriptsViewProvider,
} from "../luaDebug/luaScriptsViewProvider";

import type { HostDeps } from "./host";

export function register(
    context: vscode.ExtensionContext,
    deps: HostDeps,
): void {
    const { folderProject, gatewayFor, lsp } = deps;

    // The Lua debugger. Both registrations stay in place whatever the feature flag says: the
    // debug type must exist for launch.json to validate, and a disabled feature answers with the
    // setting to turn on rather than by vanishing.
    context.subscriptions.push(
        vscode.debug.registerDebugConfigurationProvider(
            LUA_DEBUG_TYPE,
            new LuaDebugConfigurationProvider((folder) => {
                const project =
                    folder === undefined ? undefined : folderProject(folder);
                return project === undefined
                    ? lsp
                    : gatewayFor(project.spec.key);
            }),
        ),
        vscode.debug.registerDebugAdapterDescriptorFactory(
            LUA_DEBUG_TYPE,
            new LuaDebugAdapterDescriptorFactory(
                context.extensionPath,
                logLine,
            ),
        ),
    );

    // The scripts view follows whichever eaw-lua session is active; with none it says so in place.
    const luaScripts = new LuaScriptsViewProvider();
    context.subscriptions.push(
        luaScripts,
        vscode.commands.registerCommand(
            "aet-eaw-edit.luaDebug.refreshScripts",
            () => luaScripts.refresh(),
        ),
        vscode.commands.registerCommand(
            "aet-eaw-edit.luaDebug.refreshThreads",
            (item?: LuaScriptsTreeItem) => luaScripts.refreshThreads(item),
        ),
        vscode.commands.registerCommand(
            "aet-eaw-edit.luaDebug.breakScript",
            (item?: LuaScriptsTreeItem) => luaScripts.breakScript(item),
        ),
        vscode.commands.registerCommand(
            "aet-eaw-edit.luaDebug.breakThread",
            (item?: LuaScriptsTreeItem) => luaScripts.breakThread(item),
        ),
    );
}
