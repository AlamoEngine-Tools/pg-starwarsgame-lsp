// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Runs the preview's own HLSL-to-GLSL pipeline over effects from the local shader sources and
// writes the GLSL pairs to `out/glsl/`. Usage: `node translate.js [Name.fx ...]`; no names means
// every .fx in the sources. Prints the technique, pass state and any refusal per effect.
const fs = require("node:fs");
const path = require("node:path");
const { CLIENT, OUT, readShaderSources, bundle } = require("./paths");

const pipeFile = path.join(OUT, "fxpipe.js");
bundle(path.join(CLIENT, "src", "webview", "preview", "fx", "harnessEntry.ts"), pipeFile, "node");
const pipe = require(pipeFile);

const sources = readShaderSources();
const read = (name) => sources[name.toLowerCase()] ?? null;
const glslDir = path.join(OUT, "glsl");
fs.rmSync(glslDir, { recursive: true, force: true });
fs.mkdirSync(glslDir, { recursive: true });

const names = process.argv.length > 2
    ? process.argv.slice(2)
    : Object.keys(sources).filter((n) => n.endsWith(".fx"));

let translated = 0;
let refused = 0;
for (const name of names) {
    const source = read(name);
    if (source === null) {
        console.log(`=== ${name}\nNOT FOUND`);
        continue;
    }
    const manifest = pipe.parseFxManifest(source);
    const technique = pipe.selectTechnique(manifest);
    const pass = technique && technique.passes.length > 0 ? technique.passes[0] : null;
    const state = pass ? pipe.materialStateFrom(pass) : null;
    const result = pipe.translateEffect(source, read);
    console.log(`=== ${name}`);
    console.log(`technique: ${technique ? technique.name : "none"} state=${JSON.stringify(state)}`);
    console.log(`refusal: ${result.refusal ?? "none"} missingIncludes: ${JSON.stringify(result.missingIncludes)}`);
    if (result.effect) {
        translated++;
        fs.writeFileSync(path.join(glslDir, name + ".vert.glsl"), result.effect.vertex);
        fs.writeFileSync(path.join(glslDir, name + ".frag.glsl"), result.effect.fragment);
    } else {
        refused++;
    }
}
console.log(`translated ${translated}, refused ${refused}, glsl in ${glslDir}`);
