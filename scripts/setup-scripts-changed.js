#!/usr/bin/env node
// Says whether a pipeline run has to exercise the workspace setup scripts' parity harness
// (scripts/workspace-setup.test.js). Prints one line for GITHUB_OUTPUT: changed=true or changed=false.
//
// The harness runs both setup scripts through 38 scenarios and was the slowest step of the Windows
// job - measured at 244 s of a 14-minute run - while the two scripts it reads change a few times a
// year. On a pull request that touches none of its inputs it is skipped; the pipeline's own
// "Set up workspace" step still runs the real script on both operating systems in every run.
//
// Everything that is not a pull request counts as changed: a push to master, a release and a manual
// run keep the full harness. So does a pull request whose changes cannot be read.
//
// Usage: node scripts/setup-scripts-changed.js >> "$GITHUB_OUTPUT"

'use strict';

const {execFileSync} = require('node:child_process');
const path = require('node:path');

/** What the harness reads, the harness itself, this gate, and the workflow that applies it. */
const INPUTS = [
    /^setup-workspace\.(sh|ps1)$/,
    /^scripts\/workspace-setup\.test\.js$/,
    /^scripts\/setup-scripts-changed(\.test)?\.js$/,
    /^\.github\/workflows\/test\.yml$/,
];

/** Whether any of the changed files (repo-relative, forward slashes) is an input of the harness. */
function touchesSetupScripts(files) {
    return files.some((file) => INPUTS.some((input) => input.test(file)));
}

/**
 * The files a pull request changes. A pull request run checks out the merge of the pull request
 * into its base, whose first parent is the base - so the difference to that parent is exactly what
 * the pull request brings.
 */
function pullRequestFiles(root) {
    const out = execFileSync('git', ['diff', '--name-only', 'HEAD^1', 'HEAD'], {cwd: root, encoding: 'utf8'});
    return out.split(/\r?\n/).filter(Boolean);
}

/** Whether the harness has to run: always outside a pull request, and when the changes cannot be read. */
function harnessNeeded(event, readFiles) {
    if (event !== 'pull_request') return true;
    try {
        return touchesSetupScripts(readFiles());
    } catch {
        return true;
    }
}

if (require.main === module) {
    const root = path.resolve(__dirname, '..');
    console.log(`changed=${harnessNeeded(process.env.GITHUB_EVENT_NAME, () => pullRequestFiles(root))}`);
}

module.exports = {touchesSetupScripts, harnessNeeded};
