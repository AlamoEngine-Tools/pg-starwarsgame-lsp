// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Builds and runs `render-probe.ts` headless and saves its frames to `out/`.
//
// Usage: `node render.js [--field <system.json> --textures <dir> [--wind <heading>,<speed>]]`.
// Without a field the probe draws a synthetic bump emitter. The field JSON is what
// `dump-particle` writes - the DTO the webview receives - and the textures directory is where its
// .dds files are (a .tga name resolves to the .dds of the same stem, as the server resolves it).
// `--wind` is the viewer's wind setting, heading in degrees and speed, for a field that reads it.
const fs = require("node:fs");
const path = require("node:path");
const {
    RIG,
    OUT,
    readShaderSources,
    bundle,
    dumpDom,
    unescapeHtml,
} = require("./paths");

const args = process.argv.slice(2);
const option = (name) => {
    const at = args.indexOf(name);
    return at === -1 ? null : args[at + 1];
};

const probeFile = path.join(OUT, "render-probe.js");
bundle(path.join(RIG, "render-probe.ts"), probeFile, "browser");

const sources = readShaderSources();
let fieldScript = "";
const fieldPath = option("--field");
if (fieldPath !== null) {
    const field = JSON.parse(fs.readFileSync(fieldPath, "utf8"));
    const dir = option("--textures");
    const files = dir === null ? [] : fs.readdirSync(dir);
    const textures = {};
    for (const e of field.emitters) {
        for (const name of [e.colorTexture, e.normalTexture]) {
            if (!name || textures[name.toLowerCase()]) continue;
            const stem = name.toLowerCase().replace(/\.[^.]+$/, "");
            const file =
                files.find((f) => f.toLowerCase() === `${stem}.dds`) ??
                files.find((f) => f.toLowerCase() === name.toLowerCase());
            if (file === undefined) {
                console.log(`no texture file for ${name}`);
                continue;
            }
            textures[name.toLowerCase()] = {
                format: path.extname(file).slice(1).toLowerCase(),
                data: fs.readFileSync(path.join(dir, file)).toString("base64"),
            };
        }
    }
    // `--wind <heading>,<speed>`: the viewer's wind setting, for a field that reads it.
    const windOption = option("--wind");
    const wind =
        windOption === null
            ? null
            : (() => {
                  const [heading, speed] = windOption.split(",").map(Number);
                  return { heading, speed };
              })();
    fieldScript = `<script id="field" type="application/json">${JSON.stringify({ field, textures, wind }).replace(/<\//g, "<\\/")}</script>`;
}

const html = `<!doctype html>
<html><head><meta charset="utf-8"><title>render</title></head>
<body style="margin:0;background:#202020"><pre id="out">pending</pre>
<script id="shaders" type="application/json">${JSON.stringify(sources).replace(/<\//g, "<\\/")}</script>${fieldScript}
<script>${fs.readFileSync(probeFile, "utf8").replace(/<\/script>/g, "<\\/script>")}</script>
</body></html>`;
const htmlPath = path.join(OUT, "render.html");
fs.writeFileSync(htmlPath, html);

const dom = dumpDom(htmlPath, 90000);
const report = /<pre id="out">([\s\S]*?)<\/pre>/.exec(dom);
console.log(unescapeHtml(report ? report[1] : "no output"));

const gallery = /<div id="shots">([\s\S]*?)<\/div>/.exec(dom);
const names = ["translated", "hand", "translated_again"];
if (gallery) {
    unescapeHtml(gallery[1])
        .split("\n")
        .forEach((url, i) => {
            if (!url.startsWith("data:image/png;base64,")) return;
            const file = path.join(OUT, `shot_${names[i] ?? i}.png`);
            fs.writeFileSync(file, Buffer.from(url.split(",", 2)[1], "base64"));
            console.log(file);
        });
}
