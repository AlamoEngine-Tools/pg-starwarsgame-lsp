// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Fails the build on a non-ASCII character inside a string literal, in either tree.
//
// The rule it enforces is in `asciiStringRules`, with its own tests. This is the walk: which files
// are scanned, and which are deliberately not.
//
// It reaches out of the client package and into the server's C#, because this is the only check
// that runs over both and the rule is one rule. A server-side equivalent would have to be kept in
// step with this one by hand, and the first thing to drift would be the exemption list.

const fs = require('fs');
const path = require('path');

const { stringOffences, LANGS } = require('./asciiStringRules');

const CLIENT = path.join(__dirname, '..', 'src');
const REPO = path.join(__dirname, '..', '..', '..');

/** Build output, dependencies, the game's own data and the vendored schema repository. */
const SKIP_DIRS = new Set([
    'node_modules', 'out', 'obj', 'bin', '.git', '.vs', 'dist',
    'schema', 'foc', 'eaw', 'logs', 'resources',
]);

/**
 * Test sources, where a non-ASCII literal is usually the point.
 *
 * `translationNewRow.test.ts` folds `TEST_A-umlaut` to the engine's `TEST_?` and cannot assert that
 * without the umlaut; the localisation tests read real game strings. A test string is never shown
 * to anyone.
 */
function isTest(file) {
    return /\.test\.tsx?$/.test(file) || /Test/.test(file);
}

function sources(dir, extensions) {
    let entries;

    try {
        entries = fs.readdirSync(dir, { withFileTypes: true });
    } catch {
        return [];
    }

    return entries.flatMap(entry => {
        const full = path.join(dir, entry.name);

        if (entry.isDirectory()) {
            return SKIP_DIRS.has(entry.name) ? [] : sources(full, extensions);
        }

        return extensions.some(ext => entry.name.endsWith(ext)) && !isTest(full) ? [full] : [];
    });
}

const files = [
    ...sources(CLIENT, ['.ts', '.tsx']).map(file => [file, LANGS.ts]),
    ...sources(REPO, ['.cs']).map(file => [file, LANGS.cs]),
];

let failed = 0;

for (const [file, lang] of files) {
    const text = fs.readFileSync(file, 'utf8');
    const where = path.relative(REPO, file);

    for (const offence of stringOffences(text, { lang })) {
        const hint = offence.suggestion === null ? '' : ` - write ${offence.suggestion}`;

        console.error(`${where}:${offence.line}  '${offence.char}' in a string literal${hint}`);
        failed++;
    }
}

if (failed > 0) {
    console.error(`\n${failed} non-ASCII character${failed === 1 ? '' : 's'} in strings a reader sees.`);
    console.error('Labels, tooltips, diagnostics, hover text and log messages are ASCII: use');
    console.error("'-', '...' and '->'. Comments are not scanned, so a divider run is fine.");
    process.exit(1);
}
