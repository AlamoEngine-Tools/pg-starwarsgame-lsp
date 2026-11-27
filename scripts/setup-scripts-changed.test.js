// Tests for scripts/setup-scripts-changed.js and for the skip it drives in the setup scripts'
// harness. Run: node --test scripts/setup-scripts-changed.test.js

'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const {spawnSync} = require('node:child_process');
const {touchesSetupScripts, harnessNeeded} = require('./setup-scripts-changed');

const REPO = path.resolve(__dirname, '..');

test('each input of the harness counts as a change to the setup scripts', () => {
    for (const file of [
        'setup-workspace.sh',
        'setup-workspace.ps1',
        'scripts/workspace-setup.test.js',
        'scripts/setup-scripts-changed.js',
        'scripts/setup-scripts-changed.test.js',
        '.github/workflows/test.yml',
    ]) {
        assert.equal(touchesSetupScripts([file]), true, file);
    }
});

test('the inputs the harness reads exist under those names', () => {
    // A renamed setup script would leave the gate watching a file that is no longer there.
    for (const file of ['setup-workspace.sh', 'setup-workspace.ps1', 'scripts/workspace-setup.test.js', '.github/workflows/test.yml']) {
        assert.ok(fs.existsSync(path.join(REPO, file)), file);
    }
});

test('a change elsewhere does not count, also beside or below a file that does', () => {
    assert.equal(
        touchesSetupScripts([
            'PG.StarWarsGame.LSP.Server/Program.cs',
            'PG.StarWarsGame.LSP.Client.VSCode/aet-eaw-edit/scripts/workspace-setup.test.js',
            'scripts/release-version.js',
            'docs/setup-workspace.sh',
            'setup-workspace.sh.bak',
            '.github/workflows/release.yml',
            'workspace.deps',
        ]),
        false,
    );
    assert.equal(touchesSetupScripts([]), false);
});

test('only a pull request can skip the harness', () => {
    const untouched = () => ['README.md'];
    assert.equal(harnessNeeded('pull_request', untouched), false);
    for (const event of ['push', 'workflow_dispatch', 'workflow_call', undefined]) {
        assert.equal(harnessNeeded(event, untouched), true, String(event));
    }
});

test('a pull request that touches a setup script runs the harness', () => {
    assert.equal(harnessNeeded('pull_request', () => ['README.md', 'setup-workspace.ps1']), true);
});

test('a pull request whose changes cannot be read runs the harness', () => {
    assert.equal(
        harnessNeeded('pull_request', () => {
            throw new Error('not a merge commit');
        }),
        true,
    );
});

test('the harness skips every scenario when told the setup scripts did not change, and says why', () => {
    // Without the runner's own marker: with it the child reports to this run instead of printing.
    const {NODE_TEST_CONTEXT: _, ...env} = process.env;
    const run = spawnSync(
        process.execPath,
        ['--test', '--test-reporter=spec', path.join('scripts', 'workspace-setup.test.js')],
        {
            cwd: REPO,
            encoding: 'utf8',
            env: {...env, SETUP_SCRIPTS_CHANGED: 'false'},
            // The whole harness takes minutes. Skipped, it only has to load.
            timeout: 60_000,
        },
    );
    const output = `${run.stdout}${run.stderr}`;

    assert.equal(run.status, 0, output.slice(-2000));
    assert.match(output, /neither setup script changed in this pull request/);
    assert.match(output, /^. pass 0$/m);
    assert.doesNotMatch(output, /^. skipped 0$/m);
});
