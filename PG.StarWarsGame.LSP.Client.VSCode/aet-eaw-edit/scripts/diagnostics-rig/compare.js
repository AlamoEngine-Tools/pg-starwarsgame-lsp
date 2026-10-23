// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Sets two measure.js runs against each other: per code, the count before and after; per document,
// what appeared and what went.
//
// Usage: `node compare.js <before.json> <after.json> [documents to list, default 15]`
const fs = require("node:fs");

const [beforeFile, afterFile, listArg] = process.argv.slice(2);
const before = JSON.parse(fs.readFileSync(beforeFile, "utf8"));
const after = JSON.parse(fs.readFileSync(afterFile, "utf8"));
const list = Number(listArg ?? 15);

console.log(`total ${before.total} -> ${after.total} (${before.reason}, ${after.reason})`);
const codes = [...new Set([...Object.keys(before.byCode), ...Object.keys(after.byCode)])].sort();
for (const code of codes) {
    const a = before.byCode[code] ?? 0;
    const b = after.byCode[code] ?? 0;
    if (a !== b) console.log(`  ${code}: ${a} -> ${b}`);
}

const kind = (uri) => uri.slice(uri.lastIndexOf(".") + 1).toLowerCase();
const byKind = {};
for (const [label, run] of [["before", before], ["after", after]]) {
    for (const [uri, diagnostics] of Object.entries(run.documents)) {
        byKind[kind(uri)] ??= {before: 0, after: 0};
        byKind[kind(uri)][label] += diagnostics.length;
    }
}
console.log("by file kind:");
for (const [k, v] of Object.entries(byKind)) console.log(`  .${k}: ${v.before} -> ${v.after}`);

const changed = [];
for (const uri of new Set([...Object.keys(before.documents), ...Object.keys(after.documents)])) {
    const a = before.documents[uri] ?? [];
    const b = after.documents[uri] ?? [];
    const gone = a.filter((d) => !b.includes(d));
    const added = b.filter((d) => !a.includes(d));
    if (gone.length + added.length > 0) changed.push({uri, gone, added});
}
console.log(`documents changed: ${changed.length}`);
for (const c of changed.slice(0, list)) {
    console.log(`  ${c.uri}`);
    if (c.gone.length) console.log(`    - ${c.gone.slice(0, 8).join(", ")}${c.gone.length > 8 ? ` (+${c.gone.length - 8})` : ""}`);
    if (c.added.length) console.log(`    + ${c.added.slice(0, 8).join(", ")}${c.added.length > 8 ? ` (+${c.added.length - 8})` : ""}`);
}
