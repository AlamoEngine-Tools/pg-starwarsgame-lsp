// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Recognising a folder of shader sources. Kept free of `vscode` so it can be unit tested.

import * as fs from "fs";
import * as path from "path";

import { BASE_SHADERS } from "./shared/baseShaders";

/**
 * Where the shader files sit relative to the folder the user pointed at.
 *
 * The published archive contains a `Shaders/` directory, and people extract it as-is about as often
 * as they extract its contents - so accepting only one layout would reject half of correct answers
 * with a message saying the folder is wrong. The particle effects sit one folder further down, in
 * `Engine/`, unless the download flattened them. The server's `ShaderSourceResolver` looks in the
 * same four places.
 */
const SHADER_SUBDIRECTORIES = [
    "",
    "Engine",
    "Shaders",
    path.join("Shaders", "Engine"),
];

/** The extensions a shader folder holds: sources, headers and compiled effects. */
const SHADER_EXTENSIONS = [".fx", ".fxh", ".fxo"];

/**
 * What a folder holds, shader-wise.
 *
 * `base` holds the base set the preview translates; `installed` holds shaders but not that set - a
 * mod's or a game install's `Data\Art\Shaders`, compiled `.fxo` and at most a few sources; `none`
 * holds no shader at all, or is not there.
 */
export type ShaderFolderKind = "base" | "installed" | "none";

/** The lower-cased file names in a folder and the layouts' subfolders of it. */
function shaderFiles(directory: string): Set<string> {
    const names = new Set<string>();

    for (const sub of SHADER_SUBDIRECTORIES) {
        try {
            for (const name of fs.readdirSync(path.join(directory, sub))) {
                names.add(name.toLowerCase());
            }
        } catch {
            // Missing or unreadable is the same as "not here" for this question.
        }
    }

    return names;
}

/** What a folder holds: the base set, other shaders, or none. */
export function shaderFolderKind(
    directory: string | undefined,
): ShaderFolderKind {
    if (directory === undefined || directory.trim() === "") {
        return "none";
    }

    const names = shaderFiles(directory);

    if (BASE_SHADERS.every((name) => names.has(name.toLowerCase()))) {
        return "base";
    }

    return [...names].some((name) =>
        SHADER_EXTENSIONS.includes(path.extname(name)),
    )
        ? "installed"
        : "none";
}

/**
 * Whether a folder holds the base shader sources, rather than merely existing or holding one `.fx`.
 *
 * #395: a mod's shader folder passed on its one source file, and the preview then had nothing to
 * translate.
 */
export function hasShaderSources(directory: string | undefined): boolean {
    return shaderFolderKind(directory) === "base";
}
