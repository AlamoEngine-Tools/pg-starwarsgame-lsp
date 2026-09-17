// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import * as vscode from 'vscode';

import {resolveServerCommand} from '../serverCommand';

export const LUA_DEBUG_TYPE = 'eaw-lua';

export const DEBUGGER_DISABLED_MESSAGE =
    'The Lua debugger is disabled. Enable "aet-eaw-edit.features.lua.debugger" in the settings to use it';

/** Whether the Lua debugger feature flag is on. Read live so a toggle needs no reload of this module. */
export function luaDebuggerEnabled(): boolean {
    return vscode.workspace.getConfiguration('aet-eaw-edit.features').get<boolean>('lua.debugger', false);
}

/**
 * Starts one adapter process per debug session: the server binary with `--debug-adapter`,
 * speaking DAP on stdio. The adapter shares nothing with the running language server.
 */
export class LuaDebugAdapterDescriptorFactory implements vscode.DebugAdapterDescriptorFactory {
    constructor(
        private readonly _extensionPath: string,
        private readonly _log: (line: string) => void,
    ) {
    }

    createDebugAdapterDescriptor(_session: vscode.DebugSession): vscode.ProviderResult<vscode.DebugAdapterDescriptor> {
        if (!luaDebuggerEnabled()) {
            void vscode.window.showErrorMessage(DEBUGGER_DISABLED_MESSAGE);
            return undefined;
        }

        const command = resolveServerCommand(this._extensionPath, ['--debug-adapter']);
        if (!command.command) {
            void vscode.window.showErrorMessage(
                'No server executable is configured. Set "aet-eaw-edit.lsp.executable" before starting the Lua debugger');
            return undefined;
        }

        this._log(`Starting Lua debug adapter: ${command.description}`);
        return new vscode.DebugAdapterExecutable(command.command, [...command.args]);
    }
}
