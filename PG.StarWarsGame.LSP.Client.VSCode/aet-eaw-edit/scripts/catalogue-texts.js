// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Holds every catalogue to the text rules in `textRules.js`: the manifest's `package.nls.json`, the
// l10n bundle the host and webviews draw from, and the server's `ServerMessages.resx`. A label is
// told from a message by where it is used - the manifest field, the place in the code a bundle text
// is passed, the key's family in the resx - because only a label loses its full stop. Run directly,
// it fails `npm run lint` on any text that breaks a rule.

const fs = require("node:fs");
const path = require("node:path");

const {textProblems} = require("./textRules");
const {userFacingStrings} = require("./manifest-strings");

/** Manifest fields that name a thing rather than describe it. */
const MANIFEST_LABELS = new Set([
    "title",
    "shortTitle",
    "category",
    "displayName",
    "name",
    "label",
    "contextualTitle",
    "enumItemLabels",
]);

/** Server keys whose text is a label: quick-fix titles, code lenses, inlay hints, nouns. */
const SERVER_LABEL =
    /^(FixTitle|AssetKind|DiagnosticGroup|Suppression|StartupPipeline|StartupProgress)_|Action_|CodeLens_|InlayHint_|_(Noun|RepairTitle|ChapterTarget|FunctionTarget)$/;

const KEY = /^%([\w.-]+)%$/;

function unescapeXml(text) {
    return text
        .replace(/&lt;/g, "<")
        .replace(/&gt;/g, ">")
        .replace(/&quot;/g, '"')
        .replace(/&apos;/g, "'")
        .replace(/&amp;/g, "&");
}

/** The entries of a `.resx` document. */
function resxEntries(xml) {
    const entries = [];
    const data = /<data name="([^"]+)"[^>]*>\s*<value>([\s\S]*?)<\/value>/g;
    for (let m = data.exec(xml); m !== null; m = data.exec(xml)) {
        entries.push({
            source: "ServerMessages.resx",
            key: m[1],
            text: unescapeXml(m[2]),
            label: SERVER_LABEL.test(m[1]),
        });
    }
    return entries;
}

/** The manifest's catalogue texts, each marked by the field that shows it. */
function manifestEntries(manifest, nls) {
    const seen = new Map();
    for (const s of userFacingStrings(manifest)) {
        const match = KEY.exec(s.value);
        if (match === null || !(match[1] in nls)) continue;
        const field = s.path
            .split("/")
            .filter((part) => !/^\d+$/.test(part))
            .pop();
        const entry = seen.get(match[1]) ?? {
            source: "package.nls.json",
            key: match[1],
            text: nls[match[1]],
            label: false,
        };
        entry.label = entry.label || MANIFEST_LABELS.has(field);
        seen.set(match[1], entry);
    }
    return [...seen.values()];
}

/** The bundle's texts, a label wherever any use of it is. */
function bundleEntries(bundle, calls) {
    const labels = new Set(calls.filter((c) => c.label).map((c) => c.text));
    return Object.keys(bundle).map((text) => ({
        source: "bundle.l10n.json",
        key: text,
        text: typeof bundle[text] === "string" ? bundle[text] : bundle[text].message,
        label: labels.has(text),
    }));
}

/** One line per broken rule. */
function problemsIn(entries) {
    return entries.flatMap((e) =>
        textProblems(e.text, {label: e.label}).map(
            (p) => `${e.source} ${e.key}: ${p.rule} - ${p.detail}`,
        ),
    );
}

module.exports = {resxEntries, manifestEntries, bundleEntries, problemsIn};

if (require.main === module) {
    const client = path.join(__dirname, "..");
    const repo = path.join(client, "..", "..");
    const read = (file) => fs.readFileSync(file, "utf8");
    const {catalogueCalls, hostFiles, webviewFiles} = require("./host-strings");

    const src = path.join(client, "src");
    const calls = [...hostFiles(src), ...webviewFiles(path.join(src, "webview"))].flatMap((f) =>
        catalogueCalls(f),
    );
    const entries = [
        ...manifestEntries(
            JSON.parse(read(path.join(client, "package.json"))),
            JSON.parse(read(path.join(client, "package.nls.json"))),
        ),
        ...bundleEntries(JSON.parse(read(path.join(client, "l10n", "bundle.l10n.json"))), calls),
        ...resxEntries(
            read(path.join(repo, "PG.StarWarsGame.LSP.Core", "Resources", "ServerMessages.resx")),
        ),
    ];
    const found = problemsIn(entries);
    for (const line of found) console.error(line);
    if (found.length > 0) {
        console.error(`${found.length} catalogue text(s) break the text rules (CLAUDE.md, User-facing text)`);
        process.exit(1);
    }
    console.log(`catalogue texts: ${entries.length} checked against the text rules`);
}
