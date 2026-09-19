// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import * as path from 'path';
import * as vscode from 'vscode';

/** How to start the server binary: the executable and the arguments it takes before any mode flag. */
export interface ServerCommand {
    readonly command: string;
    readonly args: readonly string[];
    /** A one-line description for the log. */
    readonly description: string;
}

/**
 * Resolves the server binary the same way for every process the extension starts from it: the
 * language server and the Lua debug adapter are the same exe, told apart by their arguments.
 * Dev mode runs from source; otherwise a `.dll` is framework-dependent and goes through `dotnet`,
 * anything else is self-contained and runs directly.
 */
export function resolveServerCommand(extensionPath: string, extraArgs: readonly string[]): ServerCommand {
    const config = (section: string) => vscode.workspace.getConfiguration(`aet-eaw-edit.${section}`);
    const devMode = config('lsp.devMode').get<boolean>('enabled', false);

    if (devMode) {
        const projectPath = config('lsp.devMode').get<string>('projectPath') ||
            path.join(extensionPath, '..', '..', 'PG.StarWarsGame.LSP.Server', 'PG.StarWarsGame.LSP.Server.csproj');
        const args = ['run', '--project', projectPath];
        if (extraArgs.length > 0) {
            args.push('--', ...extraArgs);
        }
        return {command: 'dotnet', args, description: `Dev mode: dotnet ${args.join(' ')}`};
    }

    const serverPath = config('lsp').get<string>('executable') ?? '';
    const isDll = serverPath.toLowerCase().endsWith('.dll');
    const command = isDll ? 'dotnet' : serverPath;
    const args = isDll ? [serverPath, ...extraArgs] : [...extraArgs];
    return {command, args, description: `${command} ${args.join(' ')}`};
}
