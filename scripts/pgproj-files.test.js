// Guards the checked-in .pgproj files. Run: node --test scripts/pgproj-files.test.js
//
// The extension validates every *.pgproj with the schema it ships (contributes.jsonValidation),
// which is built from the same source the server parses, so it always matches the installed
// extension. An in-file "$schema" URL overrides that association with a copy fetched from
// elsewhere - master's, a tag's - which disagrees with the extension the moment a format change
// is on a branch or a release behind. The e2e workspaces once pointed at master's copy and lit up
// `game` as "not allowed" while the branch that added it was open. No file we ship carries one.

'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const {execFileSync} = require('node:child_process');

const REPO = path.resolve(__dirname, '..');

function trackedProjectFiles() {
    const out = execFileSync('git', ['ls-files', '-z', '--', '*.pgproj'], {cwd: REPO, encoding: 'utf8'});
    return out.split('\0').filter(Boolean);
}

test('every tracked .pgproj relies on the bundled schema, none declares a $schema URL', () => {
    const files = trackedProjectFiles();
    assert.ok(files.length > 0, 'no tracked .pgproj found - is this the repository root?');
    const offenders = files.filter(rel => {
        const text = fs.readFileSync(path.join(REPO, rel), 'utf8').replace(/^﻿/, '');
        return Object.hasOwn(JSON.parse(text), '$schema');
    });
    assert.deepEqual(offenders, [], 'these project files declare a $schema URL');
});
