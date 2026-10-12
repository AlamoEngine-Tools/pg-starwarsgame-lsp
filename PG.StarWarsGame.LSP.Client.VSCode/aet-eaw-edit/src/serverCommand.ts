// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import * as vscode from "vscode";
import {
    chooseServerCommand,
    ServerChoice,
    serverEnvironment,
} from "./serverLaunch";

/** How to start the server binary: the executable and the arguments it takes before any mode flag. */
export type ServerCommand = ServerChoice;

/**
 * Resolves the server binary the same way for every process the extension starts from it: the
 * language server and the Lua debug adapter are the same exe, told apart by their arguments.
 */
export function resolveServerCommand(
    extensionPath: string,
    extraArgs: readonly string[],
): ServerCommand {
    const config = (section: string) =>
        vscode.workspace.getConfiguration(`aet-eaw-edit.${section}`);
    return chooseServerCommand({
        extensionPath,
        devMode: config("lsp.devMode").get<boolean>("enabled", false),
        projectPath: config("lsp.devMode").get<string>("projectPath") ?? "",
        executable: config("lsp").get<string>("executable") ?? "",
        extraArgs,
    });
}

/** The environment every server process starts with: the configured Lua analyzer, if any. */
export function resolveServerEnvironment(): NodeJS.ProcessEnv {
    return serverEnvironment(
        process.env,
        vscode.workspace
            .getConfiguration("aet-eaw-edit.lua")
            .get<string>("analyzerPath") ?? "",
    );
}
