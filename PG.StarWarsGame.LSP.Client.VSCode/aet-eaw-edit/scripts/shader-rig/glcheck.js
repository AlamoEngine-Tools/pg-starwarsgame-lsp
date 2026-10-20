// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Compiles and links every GLSL pair in `out/glsl/` in a REAL WebGL2 context (headless Chromium
// over SwiftShader) and reports PASS/FAIL with the compiler's own messages, each pointed at the
// translated source line. Usage: `node translate.js && node glcheck.js`.
//
// Why a browser and not the unit tests: a translated effect that does not compile is SILENT in the
// preview - three logs to the webview console and the fallback material keeps drawing - and no
// unit test has a GL. Two invisible compile failures shipped before this existed.
const fs = require("node:fs");
const path = require("node:path");
const {OUT, dumpDom, unescapeHtml} = require("./paths");

const glslDir = path.join(OUT, "glsl");
const names = [...new Set(fs.readdirSync(glslDir).filter((f) => f.endsWith(".vert.glsl")).map((f) => f.replace(/\.vert\.glsl$/, "")))];
const shaders = names.map((name) => ({
    name,
    vert: fs.readFileSync(path.join(glslDir, name + ".vert.glsl"), "utf8"),
    frag: fs.readFileSync(path.join(glslDir, name + ".frag.glsl"), "utf8"),
}));

const page = `<!doctype html>
<html><head><meta charset="utf-8"><title>glcheck</title></head>
<body><pre id="out">pending</pre>
<script id="shaders" type="application/json">${JSON.stringify(shaders).replace(/<\//g, "<\\/")}</script>
<script>
(function () {
    const out = document.getElementById("out");
    const lines = [];
    const gl = document.createElement("canvas").getContext("webgl2");
    if (!gl) { out.textContent = "FAIL no webgl2 context"; return; }
    lines.push("renderer: " + gl.getParameter(gl.RENDERER) + " / " + gl.getParameter(gl.VERSION));
    for (const s of JSON.parse(document.getElementById("shaders").textContent)) {
        const compile = (type, src) => {
            const sh = gl.createShader(type);
            gl.shaderSource(sh, src);
            gl.compileShader(sh);
            return { sh, ok: gl.getShaderParameter(sh, gl.COMPILE_STATUS), log: gl.getShaderInfoLog(sh) };
        };
        const v = compile(gl.VERTEX_SHADER, s.vert);
        const f = compile(gl.FRAGMENT_SHADER, s.frag);
        let linked = false, linkLog = "";
        if (v.ok && f.ok) {
            const p = gl.createProgram();
            gl.attachShader(p, v.sh); gl.attachShader(p, f.sh); gl.linkProgram(p);
            linked = gl.getProgramParameter(p, gl.LINK_STATUS);
            linkLog = gl.getProgramInfoLog(p);
        }
        lines.push((v.ok && f.ok && linked ? "PASS " : "FAIL ") + s.name);
        if (!v.ok) lines.push("[vertex] " + v.log);
        if (!f.ok) lines.push("[fragment] " + f.log);
        if (v.ok && f.ok && !linked) lines.push("[link] " + linkLog);
    }
    out.textContent = lines.join("\\n");
})();
</script></body></html>`;
const htmlPath = path.join(OUT, "glcheck.html");
fs.writeFileSync(htmlPath, page);

const dom = dumpDom(htmlPath, 20000);
const match = /<pre id="out">([\s\S]*?)<\/pre>/.exec(dom);
const report = unescapeHtml(match ? match[1] : "FAIL no output");

// Per failure: the compiler messages, each with the translated line it names.
let current = null;
let pass = 0;
let fail = 0;
for (const line of report.split("\n")) {
    if (line.startsWith("PASS ")) {
        pass++;
        current = null;
        continue;
    }
    if (line.startsWith("FAIL ")) {
        fail++;
        current = line.slice(5);
        console.log(`FAIL ${current}`);
        continue;
    }
    if (current === null) {
        if (line.startsWith("renderer")) console.log(line);
        continue;
    }
    const stage = /^\[(vertex|fragment|link)\]/.exec(line);
    const source = stage && stage[1] !== "link" ? fs.readFileSync(path.join(glslDir, `${current}.${stage[1] === "vertex" ? "vert" : "frag"}.glsl`), "utf8").split("\n") : null;
    const seen = new Set();
    for (const [, lineNo, message] of line.matchAll(/ERROR: 0:(\d+): ([^\n]*?)(?=ERROR: 0:|$)/g)) {
        const key = message.slice(0, 60);
        if (seen.has(key) || seen.size >= 4) continue;
        seen.add(key);
        const n = Number(lineNo);
        console.log(`  ${stage ? stage[1] : "?"} ${n}: ${message.trim().slice(0, 140)}`);
        if (source && source[n - 1] !== undefined) console.log(`      | ${source[n - 1].trim().slice(0, 140)}`);
    }
}
console.log(`GL PASS ${pass}, FAIL ${fail}`);
fs.writeFileSync(path.join(OUT, "glcheck.txt"), report);
process.exitCode = fail === 0 ? 0 : 1;
