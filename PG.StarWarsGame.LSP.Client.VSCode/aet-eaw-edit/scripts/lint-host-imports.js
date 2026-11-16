// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// No file outside src/webview imports from it (#322).
//
// The extension host is a Node build: an import of a webview module pulls that module, and everything
// it imports, into the host bundle. The first DOM reference in it then breaks the host type check, or
// gets typed away to make it pass. Code both sides need lives in src/shared, which is checked without
// the DOM (src/shared/tsconfig.json), and in src/protocol. This lint keeps the direction one way: host
// and shared code never import webview code, type-only imports included, because a type import still
// brings the file into the host's program.

const fs = require('fs');
const path = require('path');

// The tree to lint: src by default, or the folder named on the command line, which is how the test
// runs the lint over a tree with an import planted in it.
const ROOT = path.resolve(process.argv[2] || path.join(__dirname, '..', 'src'));

/** import ... from "x", export ... from "x", import "x", import("x") and require("x"). */
const SPECIFIER = /(?:\bfrom\s*|\bimport\s*\(\s*|\bimport\s+|\brequire\s*\(\s*)["']([^"']+)["']/g;

/**
 * Every import of webview code in one file's text, with its line.
 *
 * @param {string} text the file's contents
 * @param {string} file the file's absolute path
 * @param {string} root the src folder the webview folder sits in
 */
function offences(text, file, root) {
    const webview = path.join(root, 'webview') + path.sep;
    const found = [];
    for (const m of text.matchAll(SPECIFIER)) {
        const specifier = m[1];
        if (!specifier.startsWith('.')) {
            continue;
        }
        const target = path.resolve(path.dirname(file), specifier);
        if ((target + path.sep).startsWith(webview)) {
            found.push({line: text.slice(0, m.index).split('\n').length, specifier});
        }
    }
    return found;
}

/** Every .ts and .tsx file under a folder, the webview folder left out. */
function sources(dir, webview) {
    const files = [];
    for (const entry of fs.readdirSync(dir, {withFileTypes: true})) {
        const full = path.join(dir, entry.name);
        if (entry.isDirectory()) {
            if (full !== webview) {
                files.push(...sources(full, webview));
            }
        } else if (/\.tsx?$/.test(entry.name)) {
            files.push(full);
        }
    }
    return files;
}

if (require.main === module) {
    let count = 0;
    for (const file of sources(ROOT, path.join(ROOT, 'webview'))) {
        for (const o of offences(fs.readFileSync(file, 'utf8'), file, ROOT)) {
            console.error(`${path.relative(ROOT, file)}:${o.line}  imports ${o.specifier} - host and shared ` +
                'code must not import from webview/; move the module to src/shared/');
            count++;
        }
    }
    if (count > 0) {
        process.exit(1);
    }
    console.log('host imports: none from webview/');
}

module.exports = {offences};
