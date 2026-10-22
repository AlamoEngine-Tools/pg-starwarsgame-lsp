// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Runs the BUILT Mod Browser webview (`out/webview/modBrowser.js`, so `node esbuild.js` first)
// headless, answering its requests from recorded server answers, and plays a scenario of clicks.
//
// Usage: `node render.js <scenario.json> [answers.json] [--outline gameData|assets|schema]`
//
// A scenario is `[{"click": "<exact text>"}, {"type": "<text>"}, {"wait": <ms>}, ...]`; a click
// presses the first button, row, cell or tree row whose text is exactly that, a type fills the search
// form's name, and either takes `"in": "<selector>"` to look only there. `dblclick`, `menu` (a right
// click) and `hover` find their target the same way; `{"key": "F12", "on": "<text>"}` presses a key
// on it, and without `on` on the tree's focused row. Writes `out/<scenario>.png`, prints what the page shows - the rail, the breadcrumb, the
// open page, an open menu and what the page asked the host to do - and lists every request it had
// no answer for in `out/misses.json`, a requests file for record.js.
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
// An editor may save the scenario with a byte-order mark, which JSON.parse refuses.
const scenario = JSON.parse(fs.readFileSync(scenarioFile, "utf8").replace(/^﻿/, ""));
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
window.__POSTED__ = [];
window.acquireVsCodeApi = () => ({
    postMessage(message) {
        if (message.type === "ready") {
            setTimeout(() => window.postMessage({type: "outline", outline: OUTLINE}, "*"), 0);
        }
        if (["run", "openLocation", "copy", "reveal"].includes(message.type)) {
            window.__POSTED__.push(message);
        }
        if (message.type === "request") {
            const k = key(message.method, message.params);
            if (!(k in ANSWERS)) window.__MISSES__.push([message.method, message.params]);
            setTimeout(() => window.postMessage({type: "response", id: message.id, result: ANSWERS[k] ?? null}, "*"), 0);
        }
    },
});
function find(text, scope) {
    const root = scope ? document.querySelector(scope) : document;
    const hit = [...(root?.querySelectorAll("button, tr, th, td, b, [role=treeitem] .label") ?? [])]
        .find((el) => el.textContent.trim() === text)
        ?? [...(root?.querySelectorAll("[title]") ?? [])].find((el) => el.title === text);
    if (!hit) { window.__MISSES__.push(["click", text]); return null; }
    return hit.closest("button, tr, th, [role=treeitem]") ?? hit;
}
function press(text, scope) {
    find(text, scope)?.click();
}
// A pointer event at the middle of the target, as the browser would send it.
function point(type, text, scope) {
    const el = find(text, scope);
    if (!el) return;
    const box = el.getBoundingClientRect();
    const at = {bubbles: true, cancelable: true, clientX: box.left + box.width / 2, clientY: box.top + box.height / 2};
    if (type === "hover") {
        el.dispatchEvent(new MouseEvent("mouseover", {...at, relatedTarget: null}));
    } else if (type === "dblclick") {
        el.dispatchEvent(new MouseEvent("dblclick", {...at, detail: 2}));
    } else {
        el.dispatchEvent(new MouseEvent("contextmenu", {...at, button: 2}));
    }
}
// Without a target, the key goes to the tree's focused row, as the keyboard's would.
function pressKey(name, text, scope) {
    const el = text === undefined ? document.querySelector("[role=tree] [tabindex='0']") : find(text, scope);
    if (!el) return;
    el.focus();
    el.dispatchEvent(new KeyboardEvent("keydown", {key: name, bubbles: true, cancelable: true}));
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
    lines.push("--- tabs: " + [...document.querySelectorAll(".editor-tabs .editor-tab")].map((t) => t.textContent.trim()
        + (t.classList.contains("on") ? " [on]" : "") + (t.classList.contains("unpinned") ? " [preview]" : "")).join(" | "));
    const page = document.querySelector(".entry") ?? document.querySelector(".data-table");
    lines.push("--- page:");
    for (const el of page?.querySelectorAll("h1, p, tr") ?? []) {
        lines.push((el.classList.contains("selected") ? "* " : "  ") + el.textContent.trim().replace(/\\s+/g, " "));
    }
    lines.push("--- card: " + (document.querySelector(".entry .card") ? "drawn" : "none"));
    for (const tile of document.querySelectorAll(".preview-grid > section")) {
        const kind = tile.classList.contains("entry-head") ? "identity" : tile.getAttribute("aria-label");
        const area = tile.style.gridColumn + " x " + tile.style.gridRow;
        const head = [...tile.querySelectorAll(":scope > .dock-section-title .dock-section-name, :scope > .dock-section-title .section-count")]
            .map((e) => e.textContent.trim()).join(" ");
        const body = [...tile.querySelectorAll(".entry-title, .tag, dt, dd, .entry-name, .problem-msg, .panel-foot")]
            .map((e) => e.textContent.trim()).slice(0, 8).join(" | ");
        lines.push("--- tile " + kind + " (" + area + ") " + head + ": " + body
            + (tile.querySelector(".card") ? " [card]" : "") + (tile.querySelector(".code") ? " [code]" : ""));
    }
    const previewActions = document.querySelector(".preview-actions");
    if (previewActions) {
        lines.push("--- preview actions" + (previewActions.closest(".preview.beside") ? " (beside)" : " (above)") + ": "
            + [...previewActions.querySelectorAll(".dock-tile")].map((b) => b.textContent.trim() + (b.disabled ? " [off]" : "")).join(" | "));
    }
    const actions = [...document.querySelectorAll(".entry .dock-tile")];
    if (actions.length) {
        lines.push("--- page actions: " + actions.map((b) => b.textContent.trim() + (b.disabled ? " [" + b.title + "]" : "")).join(" | "));
    }
    for (const icons of document.querySelectorAll(".data-table .row-actions")) {
        const row = icons.closest("tr")?.querySelector("td")?.textContent.trim();
        lines.push("--- hover icons on " + row + ": " + [...icons.querySelectorAll("button")]
            .map((b) => (b.getAttribute("aria-label") ?? b.title) + (b.disabled ? " [" + b.title + "]" : "")).join(" | "));
    }
    const menu = document.querySelector(".row-menu");
    if (menu) {
        lines.push("--- menu:");
        for (const item of menu.querySelectorAll("button, .sep")) {
            if (item.classList.contains("sep")) { lines.push("  ----"); continue; }
            const keys = item.querySelector(".keys")?.textContent ?? "";
            const label = item.textContent.slice(0, item.textContent.length - keys.length);
            lines.push("  " + (item.disabled ? "(off) " : "") + label + (keys ? "  [" + keys + "]" : "")
                + (item === document.activeElement ? "  <- focus" : ""));
        }
    }
    lines.push("--- posted: " + JSON.stringify(window.__POSTED__));
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
    const run = step.type !== undefined ? () => type(step.type, step.in)
        : step.key !== undefined ? () => pressKey(step.key, step.on, step.in)
        : step.dblclick !== undefined ? () => point("dblclick", step.dblclick, step.in)
        : step.menu !== undefined ? () => point("menu", step.menu, step.in)
        : step.hover !== undefined ? () => point("hover", step.hover, step.in)
        : () => press(step.click, step.in);
    setTimeout(run, t);
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
    --vscode-tree-indentGuidesStroke: #585858; --vscode-list-focusOutline: #0078d4;
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
// The codicon stylesheet every panel host links, for the meanings drawn as codicons.
const codicons = "file:///" + path.join(CLIENT, "out", "codicons", "codicon.css").replace(/\\/g, "/");
fs.writeFileSync(page, `<!doctype html><html><head><meta charset="utf-8"><link rel="stylesheet" href="${codicons}"><style>${palette}</style></head><body>
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
