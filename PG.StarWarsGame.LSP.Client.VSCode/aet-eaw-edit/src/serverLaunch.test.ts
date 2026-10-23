// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import { test } from "node:test";
import * as assert from "node:assert/strict";
import * as l10n from "@vscode/l10n";
import * as path from "path";
import {
    ANALYZER_VARIABLE,
    analyzerProblem,
    bundledServerPath,
    chooseServerCommand,
    serverEnvironment,
    serverProblem,
} from "./serverLaunch";

const extensionPath = path.join("c:", "ext");
const bundled = bundledServerPath(extensionPath);

test("the bundled server lives in the extension, the analyzer beside it", () => {
    assert.equal(
        bundled,
        path.join(extensionPath, "server", "PG.StarWarsGame.LSP.Server.exe"),
    );
});

test("no executable configured: the bundled server", () => {
    const command = chooseServerCommand({
        extensionPath,
        devMode: false,
        executable: "",
        extraArgs: ["--x"],
    });
    assert.equal(command.command, bundled);
    assert.deepEqual(command.args, ["--x"]);
    assert.equal(command.bundled, true);
});

test("a configured executable wins over the bundled one", () => {
    const command = chooseServerCommand({
        extensionPath,
        devMode: false,
        executable: "d:/s/server.exe",
        extraArgs: [],
    });
    assert.equal(command.command, "d:/s/server.exe");
    assert.equal(command.bundled, false);
});

test("a configured dll runs through dotnet", () => {
    const command = chooseServerCommand({
        extensionPath,
        devMode: false,
        executable: "d:/s/Server.dll",
        extraArgs: ["--x"],
    });
    assert.equal(command.command, "dotnet");
    assert.deepEqual(command.args, ["d:/s/Server.dll", "--x"]);
});

test("dev mode runs the project from source", () => {
    const command = chooseServerCommand({
        extensionPath,
        devMode: true,
        executable: "ignored.exe",
        extraArgs: ["--x"],
    });
    assert.equal(command.command, "dotnet");
    assert.deepEqual(command.args.slice(0, 2), ["run", "--project"]);
    assert.deepEqual(command.args.slice(-2), ["--", "--x"]);
});

test("a server on disk is no problem", () => {
    const command = chooseServerCommand({
        extensionPath,
        devMode: false,
        executable: "",
        extraArgs: [],
    });
    assert.equal(
        serverProblem(command, () => true),
        undefined,
    );
});

test("a missing bundled server names itself and the override", () => {
    const command = chooseServerCommand({
        extensionPath,
        devMode: false,
        executable: "",
        extraArgs: [],
    });
    const problem = serverProblem(command, () => false);
    assert.match(problem ?? "", /bundled server is missing/);
    assert.match(problem ?? "", /aet-eaw-edit\.lsp\.executable/);
});

test("a missing configured server names its path", () => {
    const command = chooseServerCommand({
        extensionPath,
        devMode: false,
        executable: "d:/s/server.exe",
        extraArgs: [],
    });
    assert.equal(
        serverProblem(command, () => false),
        "EaWEdit: LSP server not found - d:/s/server.exe",
    );
});

// The default project path only exists when the extension runs from its source folder; an
// installed copy resolves it next to the extensions folder, where `dotnet run` exits at once.
test("dev mode without its project names the path", () => {
    const command = chooseServerCommand({
        extensionPath,
        devMode: true,
        executable: "",
        extraArgs: [],
    });
    const problem = serverProblem(command, () => false);
    assert.match(problem ?? "", /dev mode project not found/i);
    assert.match(problem ?? "", /PG\.StarWarsGame\.LSP\.Server\.csproj/);
    assert.match(problem ?? "", /aet-eaw-edit\.lsp\.devMode\.projectPath/);
});

test("dev mode with its project is no problem", () => {
    const command = chooseServerCommand({
        extensionPath,
        devMode: true,
        executable: "",
        extraArgs: [],
    });
    assert.equal(
        serverProblem(command, () => true),
        undefined,
    );
});

test("a configured dll is checked on disk, not dotnet", () => {
    const command = chooseServerCommand({
        extensionPath,
        devMode: false,
        executable: "d:/s/Server.dll",
        extraArgs: [],
    });
    assert.equal(
        serverProblem(command, (file) => file !== "d:/s/Server.dll"),
        "EaWEdit: LSP server not found - d:/s/Server.dll",
    );
});

test("a configured analyzer reaches the server as its variable", () => {
    const env = serverEnvironment({ PATH: "p" }, "d:/tools/emmylua_ls.exe");
    assert.equal(env[ANALYZER_VARIABLE], "d:/tools/emmylua_ls.exe");
    assert.equal(env.PATH, "p");
});

test("no analyzer configured: the environment as it is, so the bundled one is found", () => {
    const base = { PATH: "p", [ANALYZER_VARIABLE]: "from-shell" };
    assert.deepEqual(serverEnvironment(base, ""), base);
});

// A configured analyzer that is not there was skipped without a word, and the bundled one ran
// instead: the setting looked honoured and was not.
test("a configured analyzer that does not exist is the problem, named by its path", () => {
    assert.equal(
        analyzerProblem("d:/tools/emmylua_ls.exe", () => false),
        "d:/tools/emmylua_ls.exe",
    );
});

// What is shown when the server cannot start reads in the editor's language.
test("the reason the server cannot start comes from the catalogue", () => {
    l10n.config({
        contents: { "EaWEdit: LSP server not found - {0}": "TRANSLATED {0}" },
    });
    try {
        const command = chooseServerCommand({
            extensionPath,
            devMode: false,
            projectPath: "",
            executable: "d:/s/Server.dll",
            extraArgs: [],
        });
        assert.equal(
            serverProblem(command, () => false),
            "TRANSLATED d:/s/Server.dll",
        );
    } finally {
        l10n.config({ contents: {} });
    }
});

test("an analyzer that exists, or none configured, is no problem", () => {
    assert.equal(
        analyzerProblem("d:/tools/emmylua_ls.exe", () => true),
        undefined,
    );
    assert.equal(
        analyzerProblem("", () => false),
        undefined,
    );
});
