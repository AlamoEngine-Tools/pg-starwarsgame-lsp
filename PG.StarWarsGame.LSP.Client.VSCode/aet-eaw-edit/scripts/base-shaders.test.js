// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The client and the server decide alike whether a folder holds the base shader sources (#395).
//
// The setup command asks the extension's copy of the base set, the preview's note asks the server's.
// Two lists in two languages drift apart quietly: the command would call a folder found that the
// server then reports as holding no base shaders, or the other way round.

const assert = require("node:assert/strict");
const {describe, it} = require("node:test");
const fs = require("node:fs");
const path = require("node:path");

const repo = path.join(__dirname, "..", "..", "..");

/** The quoted shader file names in a slice of source text, sorted. */
function shaderNames(text) {
    return [...text.matchAll(/"([\w.]+\.fxh?)"/g)].map((m) => m[1]).sort();
}

/** The client's base set: the header and the particle effects, as src/shared/baseShaders.ts lists them. */
function clientSet() {
    const text = fs.readFileSync(path.join(__dirname, "..", "src", "shared", "baseShaders.ts"), "utf8");
    return [...new Set(shaderNames(text.slice(text.indexOf("export const SHADER_HEADER"))))].sort();
}

/** The server's base set, as ShaderSourceResolver.BaseShaders lists it. */
function serverSet() {
    const text = fs.readFileSync(
        path.join(repo, "PG.StarWarsGame.LSP.Server", "Assets", "ShaderSourceResolver.cs"),
        "utf8",
    );
    const start = text.indexOf("IReadOnlyList<string> BaseShaders");
    assert.notEqual(start, -1, "ShaderSourceResolver declares no BaseShaders");
    return shaderNames(text.slice(start, text.indexOf("];", start)));
}

describe("the base shader set", () => {
    it("is the same list on the client and the server", () => {
        assert.deepEqual(serverSet(), clientSet());
    });

    it("holds the header and fourteen particle effects", () => {
        assert.equal(clientSet().length, 15);
        assert.ok(clientSet().includes("AlamoEngine.fxh"));
    });
});
