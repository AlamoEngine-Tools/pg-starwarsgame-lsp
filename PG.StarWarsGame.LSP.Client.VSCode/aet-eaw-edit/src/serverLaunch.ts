// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// How the server process is started. Pure - no `vscode` import - so the decision is unit-tested.

import * as path from "path";

/** The variable the server reads for the Lua analyzer's path before its bundled one. */
export const ANALYZER_VARIABLE = "AET_EMMYLUA_LS";

/** The server the VSIX ships; the Lua analyzer ships beside it, in `server/emmylua`. */
export function bundledServerPath(extensionPath: string): string {
    return path.join(extensionPath, "server", "PG.StarWarsGame.LSP.Server.exe");
}

export interface ServerChoice {
    readonly command: string;
    readonly args: readonly string[];
    /** True when the VSIX's own server is used, so its version matches the extension by construction. */
    readonly bundled: boolean;
    /** True when the server runs from source (`dotnet run`). */
    readonly devMode: boolean;
    /** The file that has to exist for the command to start: the exe, the `.dll`, or the project. */
    readonly requires: string;
    /** A one-line description for the log. */
    readonly description: string;
}

export interface ServerChoiceInput {
    readonly extensionPath: string;
    readonly devMode: boolean;
    /** `aet-eaw-edit.lsp.devMode.projectPath`; empty for the repository's own project. */
    readonly projectPath?: string;
    /** `aet-eaw-edit.lsp.executable`; empty for the bundled server. */
    readonly executable: string;
    readonly extraArgs: readonly string[];
}

/**
 * Dev mode runs from source. Otherwise a configured executable wins - a `.dll` is
 * framework-dependent and goes through `dotnet`, anything else runs directly - and without one the
 * bundled server runs.
 */
export function chooseServerCommand(input: ServerChoiceInput): ServerChoice {
    if (input.devMode) {
        const projectPath =
            input.projectPath ||
            path.join(
                input.extensionPath,
                "..",
                "..",
                "PG.StarWarsGame.LSP.Server",
                "PG.StarWarsGame.LSP.Server.csproj",
            );
        const args = ["run", "--project", projectPath];
        if (input.extraArgs.length > 0) {
            args.push("--", ...input.extraArgs);
        }
        return {
            command: "dotnet",
            args,
            bundled: false,
            devMode: true,
            requires: projectPath,
            description: `Dev mode: dotnet ${args.join(" ")}`,
        };
    }

    if (!input.executable) {
        const command = bundledServerPath(input.extensionPath);
        const args = [...input.extraArgs];
        return {
            command,
            args,
            bundled: true,
            devMode: false,
            requires: command,
            description: `Bundled: ${command} ${args.join(" ")}`,
        };
    }

    const isDll = input.executable.toLowerCase().endsWith(".dll");
    const command = isDll ? "dotnet" : input.executable;
    const args = isDll
        ? [input.executable, ...input.extraArgs]
        : [...input.extraArgs];
    return {
        command,
        args,
        bundled: false,
        devMode: false,
        requires: input.executable,
        description: `${command} ${args.join(" ")}`,
    };
}

/** Why the chosen server cannot start, or undefined when it can. */
export function serverProblem(
    choice: ServerChoice,
    exists: (file: string) => boolean,
): string | undefined {
    if (exists(choice.requires)) {
        return undefined;
    }
    if (choice.devMode) {
        // The default project path is right only when the extension runs from its source folder.
        return `EaWEdit: Dev mode project not found - ${choice.requires}. Set "aet-eaw-edit.lsp.devMode.projectPath", or turn dev mode off`;
    }
    return choice.bundled
        ? `EaWEdit: The bundled server is missing - ${choice.requires}. Reinstall the extension, or set "aet-eaw-edit.lsp.executable"`
        : `EaWEdit: LSP server not found - ${choice.requires}`;
}

/**
 * The server's environment: the editor's own, plus the configured Lua analyzer when there is one.
 * A variable rather than an initialization option because the server resolves the analyzer while
 * it builds its services, before it reads any option.
 */
export function serverEnvironment(
    base: NodeJS.ProcessEnv,
    analyzerPath: string,
): NodeJS.ProcessEnv {
    return analyzerPath ? { ...base, [ANALYZER_VARIABLE]: analyzerPath } : base;
}
