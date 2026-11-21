// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { after, describe, it } from "node:test";

import { hasShaderSources, shaderFolderKind } from "./shaderLayout";
import { BASE_SHADERS, SHADER_HEADER } from "./shared/baseShaders";

const roots: string[] = [];

/** A throwaway folder holding the given files, relative to it. */
function folderWith(...files: string[]): string {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "aet-shaders-"));
    roots.push(root);

    for (const file of files) {
        const full = path.join(root, file);
        fs.mkdirSync(path.dirname(full), { recursive: true });
        fs.writeFileSync(full, "// not a real shader");
    }

    return root;
}

/** The base set where the managed download puts it: every file in the folder itself. */
const FLAT = BASE_SHADERS;

/** The base set as the archive ships it: the header in Shaders/, the effects in Shaders/Engine/. */
const AS_SHIPPED = BASE_SHADERS.map((name) =>
    name === SHADER_HEADER
        ? path.join("Shaders", name)
        : path.join("Shaders", "Engine", name),
);

after(() => {
    for (const root of roots) {
        fs.rmSync(root, { recursive: true, force: true });
    }
});

describe("hasShaderSources", () => {
    it("accepts the base set extracted directly", () => {
        assert.equal(hasShaderSources(folderWith(...FLAT)), true);
    });

    it("accepts the archive extracted as-is, keeping its Shaders and Engine folders", () => {
        // Both layouts are correct answers to "extract this archive", so rejecting one would tell
        // half of users their perfectly good folder is wrong.
        assert.equal(hasShaderSources(folderWith(...AS_SHIPPED)), true);
    });

    it("rejects a mod's shader folder: compiled effects and one source of its own", () => {
        // #395: one .fx file passed, and the preview then had nothing to translate.
        const mod = folderWith(
            "AetModEffect.fx",
            "AetModEffect.fxo",
            "AetModEffectB.fxo",
        );
        assert.equal(hasShaderSources(mod), false);
    });

    it("rejects a mod's copy of the header beside its own sources", () => {
        assert.equal(
            hasShaderSources(folderWith(SHADER_HEADER, "AetModEffect.fx")),
            false,
        );
    });

    it("rejects the base set with one file missing", () => {
        assert.equal(hasShaderSources(folderWith(...FLAT.slice(1))), false);
    });

    it("rejects a folder with no shaders in it", () => {
        assert.equal(hasShaderSources(folderWith("readme.txt")), false);
    });

    it("does not go hunting deeper than one level", () => {
        // A folder whose shaders are buried three deep is more likely the wrong folder than a
        // layout worth supporting, and searching a whole drive for .fx files is not a good default.
        assert.equal(
            hasShaderSources(
                folderWith(
                    ...FLAT.map((name) => path.join("a", "b", "c", name)),
                ),
            ),
            false,
        );
    });

    it("matches the extension case-insensitively", () => {
        assert.equal(
            hasShaderSources(
                folderWith(...FLAT.map((name) => name.toUpperCase())),
            ),
            true,
        );
    });

    it("handles a folder that is not there without throwing", () => {
        assert.equal(
            hasShaderSources(path.join(os.tmpdir(), "aet-shaders-absent-xyz")),
            false,
        );
    });

    it("treats nothing configured as nothing found", () => {
        assert.equal(hasShaderSources(undefined), false);
        assert.equal(hasShaderSources("   "), false);
    });
});

describe("shaderFolderKind", () => {
    it("names the base set", () => {
        assert.equal(shaderFolderKind(folderWith(...FLAT)), "base");
        assert.equal(shaderFolderKind(folderWith(...AS_SHIPPED)), "base");
    });

    it("names a mod's folder: sources or compiled effects without the base set", () => {
        assert.equal(
            shaderFolderKind(folderWith("AetModEffect.fx", "AetModEffect.fxo")),
            "installed",
        );
        assert.equal(
            shaderFolderKind(folderWith(SHADER_HEADER, "AetModEffect.fx")),
            "installed",
        );
    });

    it("names a game install's folder, which holds compiled effects only", () => {
        assert.equal(
            shaderFolderKind(folderWith("AetModEffect.fxo")),
            "installed",
        );
    });

    it("names a folder with no shaders, a missing one and nothing configured", () => {
        assert.equal(shaderFolderKind(folderWith("readme.txt")), "none");
        assert.equal(
            shaderFolderKind(path.join(os.tmpdir(), "aet-shaders-absent-xyz")),
            "none",
        );
        assert.equal(shaderFolderKind(undefined), "none");
    });
});
