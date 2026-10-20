// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Every string a reader sees in the extension's manifest comes from `package.nls.json`, the
// catalogue VS Code localises the manifest from. This finds those strings, says which are still
// written into `package.json` itself and which `%key%` the catalogue does not carry. Run directly,
// it checks the shipped manifest and fails `npm run lint` on either.

const fs = require("node:fs");
const path = require("node:path");

/** The manifest fields a reader sees. Ids, values, `when` clauses and the like are not text. */
const TEXT_FIELDS = new Set([
    "title",
    "shortTitle",
    "category",
    "displayName",
    "description",
    "markdownDescription",
    "deprecationMessage",
    "markdownDeprecationMessage",
    "enumDescriptions",
    "markdownEnumDescriptions",
    "enumItemLabels",
    "name",
    "label",
    "contextualTitle",
    "aliases",
]);

const KEY = /^%([\w.-]+)%$/;

/**
 * Every user-facing string in a manifest, with its path.
 *
 * The package's own `name` is its id and `author` is metadata, so neither is text a reader sees.
 */
function userFacingStrings(manifest) {
    const found = [];

    const walk = (node, trail, field) => {
        if (typeof node === "string") {
            if (field !== null) {
                found.push({ path: trail.join("/"), value: node });
            }
            return;
        }
        if (Array.isArray(node)) {
            node.forEach((item, i) => walk(item, [...trail, String(i)], field));
            return;
        }
        if (node !== null && typeof node === "object") {
            for (const [key, value] of Object.entries(node)) {
                const top = trail.length === 0;
                if (top && (key === "name" || key === "author")) {
                    continue;
                }
                walk(value, [...trail, key], TEXT_FIELDS.has(key) ? key : null);
            }
        }
    };

    walk(manifest, [], null);
    return found;
}

/** The user-facing strings still written into the manifest rather than referenced by key. */
function literals(manifest) {
    return userFacingStrings(manifest).filter((s) => !KEY.test(s.value));
}

/** The `%key%` references the catalogue does not carry. */
function unresolved(manifest, nls) {
    return userFacingStrings(manifest)
        .map((s) => KEY.exec(s.value))
        .filter((match) => match !== null && !(match[1] in nls))
        .map((match) => match[1]);
}

module.exports = { userFacingStrings, literals, unresolved, TEXT_FIELDS };

if (require.main === module) {
    const client = path.join(__dirname, "..");
    const manifest = JSON.parse(fs.readFileSync(path.join(client, "package.json"), "utf8"));
    const nlsPath = path.join(client, "package.nls.json");
    const nls = fs.existsSync(nlsPath) ? JSON.parse(fs.readFileSync(nlsPath, "utf8")) : {};

    const written = literals(manifest);
    const missing = unresolved(manifest, nls);
    for (const s of written) {
        console.error(`package.json: ${s.path} is written in place; move it to package.nls.json`);
    }
    for (const key of missing) {
        console.error(`package.json: %${key}% is not in package.nls.json`);
    }
    if (written.length > 0 || missing.length > 0) {
        process.exit(1);
    }
    console.log("manifest strings: every one from package.nls.json");
}
