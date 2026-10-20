// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Every command the server handles is registered by the extension itself, which forwards it.
//
// The server announces its commands in ONE `client/registerCapability` batch, and the language client
// turns each into a VS Code command. The extension registers several of the same ids for its own
// palette entries, so that batch collides and is rejected as a whole - taking with it every command
// the extension did not also register. A quick fix naming such a command then fails with "command
// not found". Owning every one is the only arrangement that does not depend on the batch.

const assert = require("node:assert/strict");
const {describe, it} = require("node:test");
const fs = require("node:fs");
const path = require("node:path");

const repo = path.join(__dirname, "..", "..", "..");
const src = path.join(__dirname, "..", "src");

function csFiles(dir) {
    return fs.readdirSync(dir, {withFileTypes: true}).flatMap((entry) => {
        const full = path.join(dir, entry.name);
        if (entry.isDirectory()) {
            return ["bin", "obj"].includes(entry.name) ? [] : csFiles(full);
        }
        return entry.name.endsWith(".cs") ? [full] : [];
    });
}

/** The command ids the server's execute-command handlers answer, constants resolved. */
function serverCommands() {
    const sources = ["PG.StarWarsGame.LSP.Core", "PG.StarWarsGame.LSP.Server", "PG.StarWarsGame.LSP.Xml"]
        .flatMap((project) => csFiles(path.join(repo, project)))
        .map((file) => fs.readFileSync(file, "utf8"));
    const constants = new Map(
        sources.flatMap((text) =>
            [...text.matchAll(/const string (\w+) = "(aet-eaw-edit\.[\w.]+)"/g)].map((m) => [m[1], m[2]]),
        ),
    );
    return sources
        .filter((text) => text.includes(": ExecuteCommandHandlerBase"))
        .map((text) => /const string CommandName = (?:"([^"]+)"|\w+\.(\w+));/.exec(text))
        .filter((m) => m !== null)
        .map((m) => m[1] ?? constants.get(m[2]))
        .sort();
}

/** The command ids the extension registers with `vscode.commands.registerCommand`. */
function extensionCommands() {
    const text = fs
        .readdirSync(src)
        .filter((name) => name.endsWith(".ts") && !name.endsWith(".test.ts"))
        .map((name) => fs.readFileSync(path.join(src, name), "utf8"))
        .join("\n");
    return new Set([...text.matchAll(/registerCommand\(\s*"([^"]+)"/g)].map((m) => m[1]));
}

describe("server commands", () => {
    it("finds the server's command handlers", () => {
        assert.ok(serverCommands().includes("aet-eaw-edit.lsp.suppressDiagnosticGlobally"));
    });

    it("has the extension register every one of them", () => {
        const registered = extensionCommands();
        assert.deepEqual(
            serverCommands().filter((command) => !registered.has(command)),
            [],
        );
    });
});
