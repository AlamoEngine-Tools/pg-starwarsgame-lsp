// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Where the shader rig finds things. Every script in this folder goes through here, so a moved
// checkout or a different browser install is one edit.
//
// The rig reads the maintainer's local shader sources (gitignored, confidential) at run time and
// writes everything it derives from them under `out/`, which is gitignored too. Nothing derived
// from the sources is ever tracked.
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const {execFileSync} = require("node:child_process");

const RIG = __dirname;
const CLIENT = path.resolve(RIG, "..", "..");
const REPO = path.resolve(CLIENT, "..", "..");
const OUT = path.join(RIG, "out");
const SHADER_SOURCES = process.env.AET_SHADER_SOURCES ?? path.join(REPO, "shader-sources");
const SERVER_BIN = path.join(REPO, "PG.StarWarsGame.LSP.Server", "bin", "Debug", "net10.0");
const GAME = process.env.AET_GAME_DIR ?? null;

/** Every .fx and .fxh under the shader sources, keyed by lower-cased basename. */
function readShaderSources() {
    const sources = {};
    (function walk(dir) {
        for (const entry of fs.readdirSync(dir, {withFileTypes: true})) {
            const full = path.join(dir, entry.name);
            if (entry.isDirectory()) walk(full);
            else if (/\.fxh?$/i.test(entry.name)) sources[entry.name.toLowerCase()] = fs.readFileSync(full, "utf8");
        }
    })(SHADER_SOURCES);
    return sources;
}

/** Playwright's headless Chromium, which runs WebGL2 through SwiftShader without a GPU. */
function findChrome() {
    if (process.env.AET_CHROME) return process.env.AET_CHROME;
    const root = path.join(os.homedir(), "AppData", "Local", "ms-playwright");
    if (!fs.existsSync(root)) throw new Error(`no playwright browsers under ${root}; set AET_CHROME`);
    const shells = fs.readdirSync(root).filter((d) => d.startsWith("chromium_headless_shell-")).sort();
    if (shells.length === 0) throw new Error(`no chromium_headless_shell-* under ${root}; set AET_CHROME`);
    return path.join(root, shells[shells.length - 1], "chrome-headless-shell-win64", "chrome-headless-shell.exe");
}

/** Loads a page headless and returns its DOM after `budgetMs` of virtual time. */
function dumpDom(htmlPath, budgetMs) {
    const out = execFileSync(
        findChrome(),
        [
            "--headless",
            "--use-angle=swiftshader",
            "--enable-unsafe-swiftshader",
            "--no-sandbox",
            `--virtual-time-budget=${budgetMs}`,
            "--dump-dom",
            `file:///${htmlPath.replace(/\\/g, "/")}`,
        ],
        {maxBuffer: 256 * 1024 * 1024, stdio: ["ignore", "pipe", "ignore"]},
    );
    return out.toString("utf8").replace(/\r/g, "");
}

/** Bundles a TypeScript entry with the client's esbuild, for node or the browser. */
function bundle(entry, outfile, platform) {
    const esbuild = path.join(CLIENT, "node_modules", ".bin", process.platform === "win32" ? "esbuild.cmd" : "esbuild");
    const args = [entry, "--bundle", `--outfile=${outfile}`, "--log-level=error"];
    if (platform === "browser") {
        args.push("--format=iife", "--platform=browser", `--alias:three=${path.join(CLIENT, "node_modules", "three")}`, "--loader:.png=dataurl");
    } else {
        args.push("--format=cjs", "--platform=node");
    }
    execFileSync(esbuild, args, {cwd: CLIENT, stdio: "inherit", shell: process.platform === "win32"});
}

function unescapeHtml(text) {
    return text.replace(/&gt;/g, ">").replace(/&lt;/g, "<").replace(/&quot;/g, '"').replace(/&#39;/g, "'").replace(/&amp;/g, "&");
}

fs.mkdirSync(OUT, {recursive: true});

module.exports = {
    RIG,
    CLIENT,
    REPO,
    OUT,
    SHADER_SOURCES,
    SERVER_BIN,
    GAME,
    readShaderSources,
    findChrome,
    dumpDom,
    bundle,
    unescapeHtml
};
