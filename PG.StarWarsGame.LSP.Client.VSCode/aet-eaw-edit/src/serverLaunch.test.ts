// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import { test } from "node:test";
import * as assert from "node:assert/strict";
import * as path from "path";
import {
    ANALYZER_VARIABLE,
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

test("dotnet is not checked on disk", () => {
    const command = chooseServerCommand({
        extensionPath,
        devMode: true,
        executable: "",
        extraArgs: [],
    });
    assert.equal(
        serverProblem(command, () => false),
        undefined,
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
