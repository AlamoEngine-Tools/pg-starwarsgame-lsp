// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The measure that matters: which compiled effects the GAME ships (names in its shaders.meg) and
// how each fares in the translator - PASS / FAIL from `out/glcheck.txt`, refused from the last
// `translate.js` run, or absent from the sources. Usage: `node megcompare.js <path-to-shaders.meg>`
// after `translate.js > out/translate.txt` and `glcheck.js`.
const fs = require("node:fs");
const path = require("node:path");
const {OUT} = require("./paths");

const meg = fs.readFileSync(process.argv[2]);
const shipped = [...new Set([...meg.toString("latin1").matchAll(/[A-Za-z0-9_]+\.fxo/gi)].map((m) => m[0].toLowerCase()))].sort();

const gl = new Map();
for (const line of fs.readFileSync(path.join(OUT, "glcheck.txt"), "utf8").split("\n")) {
    const m = /^(PASS|FAIL) (\S+)/.exec(line);
    if (m) gl.set(m[2].toLowerCase(), m[1]);
}
const refused = new Map();
const translateLog = path.join(OUT, "translate.txt");
if (fs.existsSync(translateLog)) {
    let name = null;
    for (const line of fs.readFileSync(translateLog, "utf8").split("\n")) {
        const head = /^=== (\S+)/.exec(line);
        if (head) {
            name = head[1].toLowerCase();
            continue;
        }
        const r = /^refusal: (.*?) missingIncludes/.exec(line);
        if (r && name && r[1] !== "none") refused.set(name, r[1].slice(0, 70));
    }
}

const counts = {PASS: 0, FAIL: 0, refused: 0, absent: 0};
for (const fxo of shipped) {
    const fx = fxo.slice(0, -1);
    const state = gl.get(fx) ?? (refused.has(fx) ? "refused" : "absent");
    counts[state]++;
    if (state !== "PASS") console.log(`${state.padEnd(8)} ${fx}  ${refused.get(fx) ?? ""}`);
}
console.log(`shipped compiled effects: ${shipped.length}; ${Object.entries(counts).map(([k, v]) => `${k} ${v}`).join(", ")}`);
