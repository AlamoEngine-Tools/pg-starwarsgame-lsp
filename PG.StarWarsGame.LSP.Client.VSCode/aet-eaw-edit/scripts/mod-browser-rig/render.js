// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Runs the BUILT Mod Browser webview (`out/webview/modBrowser.js`, so `node esbuild.js` first)
// headless, answering its requests from recorded server answers, and plays a scenario of clicks.
//
// Usage: `node render.js <scenario.json> [answers.json] [--outline gameData|assets|schema]`
//
// A scenario is `[{"click": "<exact text>"}, {"type": "<text>"}, {"wait": <ms>}, ...]`; a click
// presses the first button, row or cell whose text is exactly that, a type fills the search form's
// name, and either takes `"in": "<selector>"` to look only there. Writes `out/<scenario>.png`, prints what the
// page shows - the rail, the breadcrumb, the open page - and lists every request it had no answer
// for in `out/misses.json`, a requests file for record.js.
const fs = require("node:fs");
const path = require("node:path");
const {execFileSync} = require("node:child_process");
const {OUT, CLIENT, BUNDLE, answerKey} = require("./paths");

const args = process.argv.slice(2);
const flag = (name) => {
    const at = args.indexOf(name);
    return at === -1 ? null : args.splice(at, 2)[1];
};
const outline = flag("--outline") ?? "gameData";
const [scenarioFile, answersArg] = args;
if (!scenarioFile) {
    console.error("usage: node render.js <scenario.json> [answers.json] [--outline gameData|assets|schema]");
    process.exit(2);
}
const answersFile = answersArg ?? path.join(OUT, "answers.json");
const answers = fs.existsSync(answersFile) ? JSON.parse(fs.readFileSync(answersFile, "utf8")) : {};
const scenario = JSON.parse(fs.readFileSync(scenarioFile, "utf8"));
const name = path.basename(scenarioFile, ".json");
const l10n = fs.readFileSync(path.join(CLIENT, "l10n", "bundle.l10n.json"), "utf8");

const escapeAttr = (text) => text.replace(/&/g, "&amp;").replace(/"/g, "&quot;");

// The stub stands in for modBrowserPanel: it says which outline is open once the page is ready and
// answers each relayed request from the recording. Every key is computed by paths.answerKey.
const stub = `
const ANSWERS = ${JSON.stringify(answers)};
const SCENARIO = ${JSON.stringify(scenario)};
const OUTLINE = ${JSON.stringify(outline)};
const key = ${answerKey.toString()};
window.__MISSES__ = [];
window.acquireVsCodeApi = () => ({
    postMessage(message) {
        if (message.type === "ready") {
            setTimeout(() => window.postMessage({type: "outline", outline: OUTLINE}, "*"), 0);
        }
        if (message.type === "request") {
            const k = key(message.method, message.params);
            if (!(k in ANSWERS)) window.__MISSES__.push([message.method, message.params]);
            setTimeout(() => window.postMessage({type: "response", id: message.id, result: ANSWERS[k] ?? null}, "*"), 0);
        }
    },
});
function press(text, scope) {
    const root = scope ? document.querySelector(scope) : document;
    const hit = [...(root?.querySelectorAll("button, tr, td, b") ?? [])].find((el) => el.textContent.trim() === text);
    if (!hit) { window.__MISSES__.push(["click", text]); return; }
    (hit.closest("button, tr") ?? hit).click();
}
// React tracks an input's value itself, so the native setter and an input event are what it hears.
function type(text, scope) {
    const input = document.querySelector(scope ?? ".search input");
    if (!input) { window.__MISSES__.push(["click", "input " + (scope ?? "")]); return; }
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value").set.call(input, text);
    input.dispatchEvent(new Event("input", {bubbles: true}));
}
function report() {
    const lines = [];
    for (const node of document.querySelectorAll(".rail .node")) {
        const depth = Number(node.style.getPropertyValue("--depth") || 0);
        const mark = node.classList.contains("selected") ? "*" : " ";
        lines.push(mark + "  ".repeat(depth) + node.textContent.trim());
    }
    lines.push("--- crumbs: " + [...document.querySelectorAll(".crumbs .crumb-step")].map((c) => c.textContent.trim()).join(" > "));
    const page = document.querySelector(".entry") ?? document.querySelector(".grid");
    lines.push("--- page:");
    for (const el of page?.querySelectorAll("h1, p, tr") ?? []) lines.push("  " + el.textContent.trim().replace(/\\s+/g, " "));
    lines.push("--- card: " + (document.querySelector(".entry .card") ? "drawn" : "none"));
    const pre = document.createElement("pre");
    pre.id = "rig-report";
    pre.textContent = lines.join("\\n");
    document.body.appendChild(pre);
    const misses = document.createElement("pre");
    misses.id = "rig-misses";
    misses.textContent = JSON.stringify(window.__MISSES__);
    document.body.appendChild(misses);
}
let at = 300;
for (const step of SCENARIO) {
    if (step.wait) { at += step.wait; continue; }
    const t = at;
    setTimeout(() => (step.type !== undefined ? type(step.type, step.in) : press(step.click, step.in)), t);
    at += 300;
}
setTimeout(report, at + 300);
`;

// VS Code's Dark Modern palette and its default webview control styling, the part the panel leans on.
const palette = `
:root {
    --vscode-font-family: "Segoe UI", sans-serif; --vscode-font-size: 13px;
    --vscode-foreground: #cccccc; --vscode-descriptionForeground: #9d9d9d; --vscode-disabledForeground: #6e6e6e;
    --vscode-editor-background: #1f1f1f; --vscode-sideBar-background: #181818; --vscode-panel-border: #2b2b2b;
    --vscode-list-hoverBackground: #2a2d2e; --vscode-list-activeSelectionBackground: #04395e;
    --vscode-list-activeSelectionForeground: #ffffff; --vscode-textLink-foreground: #4daafc;
    --vscode-badge-background: #616161; --vscode-badge-foreground: #f8f8f8;
    --vscode-button-background: #0078d4; --vscode-button-foreground: #ffffff; --vscode-button-hoverBackground: #026ec1;
    --vscode-button-secondaryBackground: #313131; --vscode-button-secondaryForeground: #cccccc;
    --vscode-input-background: #313131; --vscode-input-foreground: #cccccc; --vscode-input-border: #3c3c3c;
    --vscode-focusBorder: #0078d4;
}
html, body { margin: 0; padding: 0; height: 100%; background: #1f1f1f; color: #cccccc; }
#root { height: 100%; }
#rig-report, #rig-misses { display: none; }
`;

const page = path.join(OUT, `${name}.html`);
fs.writeFileSync(page, `<!doctype html><html><head><meta charset="utf-8"><style>${palette}</style></head><body>
<div id="root" data-l10n="${escapeAttr(l10n)}"></div>
<script>${stub}</script>
<script src="${"file:///" + BUNDLE.replace(/\\/g, "/")}"></script>
</body></html>`);

const chrome = (() => {
    if (process.env.AET_CHROME) return process.env.AET_CHROME;
    const root = path.join(require("node:os").homedir(), "AppData", "Local", "ms-playwright");
    const shell = fs.readdirSync(root).filter((d) => d.startsWith("chromium_headless_shell-")).sort().pop();
    return path.join(root, shell, "chrome-headless-shell-win64", "chrome-headless-shell.exe");
})();
const budget = String(2000 + scenario.length * 600);
const common = ["--headless", "--no-sandbox", "--allow-file-access-from-files", `--virtual-time-budget=${budget}`, "--window-size=1280,860"];
const url = "file:///" + page.replace(/\\/g, "/");

const dom = execFileSync(chrome, [...common, "--dump-dom", url], {
    maxBuffer: 64 * 1024 * 1024,
    stdio: ["ignore", "pipe", "ignore"]
}).toString("utf8");
execFileSync(chrome, [...common, `--screenshot=${path.join(OUT, `${name}.png`)}`, url], {stdio: "ignore"});

const unescape = (text) => text.replace(/&gt;/g, ">").replace(/&lt;/g, "<").replace(/&quot;/g, '"').replace(/&#39;/g, "'").replace(/&amp;/g, "&");
const reportText = /<pre id="rig-report">([\s\S]*?)<\/pre>/.exec(dom)?.[1];
const missesText = /<pre id="rig-misses">([\s\S]*?)<\/pre>/.exec(dom)?.[1];
console.log(reportText ? unescape(reportText) : "(no report - the page did not finish its scenario)");
const misses = missesText ? JSON.parse(unescape(missesText)) : [];
const requests = misses.filter(([method]) => method !== "click");
fs.writeFileSync(path.join(OUT, "misses.json"), JSON.stringify(requests, null, 1));
for (const [, text] of misses.filter(([method]) => method === "click")) console.log(`!! nothing to click: ${text}`);
console.log(`${requests.length} unanswered requests -> out/misses.json; screenshot -> out/${name}.png`);
