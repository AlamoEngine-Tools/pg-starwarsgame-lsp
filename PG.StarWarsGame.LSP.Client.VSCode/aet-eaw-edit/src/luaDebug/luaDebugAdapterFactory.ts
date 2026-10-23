// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import * as fs from "fs";
import * as vscode from "vscode";

import { resolveServerCommand } from "../serverCommand";
import { serverProblem } from "../serverLaunch";

export const LUA_DEBUG_TYPE = "eaw-lua";

/**
 * Starts one adapter process per debug session: the server binary with `--debug-adapter`,
 * speaking DAP on stdio. The adapter shares nothing with the running language server.
 */
export class LuaDebugAdapterDescriptorFactory
    implements vscode.DebugAdapterDescriptorFactory
{
    constructor(
        private readonly _extensionPath: string,
        private readonly _log: (line: string) => void,
    ) {}

    createDebugAdapterDescriptor(
        _session: vscode.DebugSession,
    ): vscode.ProviderResult<vscode.DebugAdapterDescriptor> {
        const command = resolveServerCommand(this._extensionPath, [
            "--debug-adapter",
        ]);
        const serverMissing = serverProblem(command, fs.existsSync);
        if (serverMissing) {
            void vscode.window.showErrorMessage(serverMissing);
            return undefined;
        }

        this._log(`Starting Lua debug adapter: ${command.description}`);
        return new vscode.DebugAdapterExecutable(command.command, [
            ...command.args,
        ]);
    }
}
