// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Every webview that renders the shared Combobox carries its rules.
//
// The Combobox's list is absolutely placed under its field by `comboboxCss`. A webview that renders
// the field without those rules gets the list as plain text in the page flow: no background, and
// opening it pushes the field itself up by the list's height, under the pointer. The encyclopedia
// preview's Screen field shipped that way.

const assert = require("node:assert/strict");
const {describe, it} = require("node:test");
const fs = require("node:fs");
const path = require("node:path");

const root = path.join(__dirname, "..");
const combobox = path.join(root, "src", "webview", "shared", "Combobox.tsx");

/** The webview entry points esbuild bundles. */
function entries() {
    const config = fs.readFileSync(path.join(root, "esbuild.js"), "utf8");
    return [...config.matchAll(/'(src\/webview\/[\w/]+\.tsx)'/g)].map((m) => path.join(root, m[1]));
}

/** The file a relative import names, or undefined when it is not a source file. */
function resolve(from, specifier) {
    const base = path.resolve(path.dirname(from), specifier);
    return [base, `${base}.ts`, `${base}.tsx`, path.join(base, "index.ts"), path.join(base, "index.tsx")].find(
        (candidate) => fs.existsSync(candidate) && fs.statSync(candidate).isFile(),
    );
}

/** Every source file an entry bundles, through its relative imports. */
function closure(entry) {
    const seen = new Set();
    const pending = [entry];
    while (pending.length > 0) {
        const file = pending.pop();
        if (seen.has(file)) {
            continue;
        }
        seen.add(file);
        const text = fs.readFileSync(file, "utf8");
        for (const m of text.matchAll(/from\s+["'](\.{1,2}\/[^"']+)["']/g)) {
            const next = resolve(file, m[1]);
            if (next !== undefined) {
                pending.push(next);
            }
        }
    }
    return seen;
}

describe("combobox css", () => {
    it("finds the webviews that render the Combobox", () => {
        assert.ok(entries().filter((entry) => closure(entry).has(combobox)).length >= 2);
    });

    it("gives every webview that renders the Combobox its rules", () => {
        assert.deepEqual(
            entries()
                .filter((entry) => {
                    const files = closure(entry);
                    return (
                        files.has(combobox) &&
                        ![...files].some((file) => fs.readFileSync(file, "utf8").includes("${comboboxCss}"))
                    );
                })
                .map((entry) => path.relative(root, entry).replace(/\\/g, "/")),
            [],
        );
    });
});
