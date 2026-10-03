// Parity harness for setup-workspace.sh and setup-workspace.ps1.
//
// Every scenario builds a throwaway workspace out of local bare repositories (file:// URLs), runs
// BOTH scripts against identical copies of it, asserts the resulting repository state, and then
// asserts that the two scripts printed the same status lines. That last check is what keeps the
// two implementations from drifting apart.
//
// Run: node --test scripts/workspace-setup.test.js

'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const {spawnSync, execFileSync} = require('node:child_process');
const {pathToFileURL} = require('node:url');

const REPO = path.resolve(__dirname, '..');
const SCRIPT_FILES = ['setup-workspace.sh', 'setup-workspace.ps1'];

// ── Environment ──────────────────────────────────────────────────────────────

// Isolate git from the machine's own configuration (insteadOf rules, autocrlf, hooks paths...)
// so a scenario behaves the same on every developer machine and on CI.
const emptyConfig = path.join(os.tmpdir(), 'workspace-setup-empty.gitconfig');
fs.writeFileSync(emptyConfig, '');
const GIT_ENV = {
    ...process.env,
    GIT_CONFIG_GLOBAL: emptyConfig,
    GIT_CONFIG_NOSYSTEM: '1',
    GIT_CONFIG_COUNT: '2',
    GIT_CONFIG_KEY_0: 'core.autocrlf',
    GIT_CONFIG_VALUE_0: 'false',
    GIT_CONFIG_KEY_1: 'protocol.file.allow',
    GIT_CONFIG_VALUE_1: 'always',
    GIT_AUTHOR_NAME: 'Fixture',
    GIT_AUTHOR_EMAIL: 'fixture@example.invalid',
    GIT_COMMITTER_NAME: 'Fixture',
    GIT_COMMITTER_EMAIL: 'fixture@example.invalid',
    GIT_TERMINAL_PROMPT: '0',
};
// The scripts must not inherit a CI flag or a step summary from the run that hosts this test.
delete GIT_ENV.GITHUB_STEP_SUMMARY;

function git(cwd, ...args) {
    return execFileSync('git', args, {cwd, env: GIT_ENV, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe']}).trim();
}

// On Windows, `bash` on PATH may be WSL's launcher. Git for Windows' own bash is the one that
// matches what a contributor runs, so locate it through git itself.
function findBash() {
    if (process.env.WORKSPACE_SETUP_BASH) return process.env.WORKSPACE_SETUP_BASH;
    if (process.platform !== 'win32') return 'bash';
    const execPath = execFileSync('git', ['--exec-path'], {encoding: 'utf8'}).trim();
    const candidate = path.resolve(execPath, '..', '..', '..', 'bin', 'bash.exe');
    return fs.existsSync(candidate) ? candidate : null;
}

function findPwsh() {
    const probe = spawnSync('pwsh', ['-NoProfile', '-Command', '$PSVersionTable.PSVersion.Major'], {encoding: 'utf8'});
    return probe.status === 0 ? 'pwsh' : null;
}

const BASH = findBash();
const PWSH = findPwsh();

const SHELLS = [
    {
        name: 'bash',
        available: BASH !== null,
        argv(fx, o) {
            const a = [toPosix(path.join(fx.lsp, 'setup-workspace.sh'))];
            if (o.command) a.push(o.command);
            if (o.groups !== undefined) a.push('--groups', o.groups);
            if (o.matchBranch !== undefined) a.push('--match-branch', o.matchBranch);
            if (o.manifest !== undefined) a.push('--manifest', toPosix(o.manifest));
            if (o.summary !== undefined) a.push('--summary', toPosix(o.summary));
            if (o.noSelfUpdate !== false) a.push('--no-self-update');
            return [BASH, a];
        },
    },
    {
        name: 'pwsh',
        available: PWSH !== null,
        argv(fx, o) {
            const a = ['-NoProfile', '-NonInteractive', '-File', path.join(fx.lsp, 'setup-workspace.ps1')];
            if (o.command) a.push(o.command);
            if (o.groups !== undefined) a.push('-Groups', o.groups);
            if (o.matchBranch !== undefined) a.push('-MatchBranch', o.matchBranch);
            if (o.manifest !== undefined) a.push('-Manifest', o.manifest);
            if (o.summary !== undefined) a.push('-Summary', o.summary);
            if (o.noSelfUpdate !== false) a.push('-NoSelfUpdate');
            return [PWSH, a];
        },
    },
];

function toPosix(p) {
    return p.split(path.sep).join('/');
}

// ── Fixture ──────────────────────────────────────────────────────────────────

function write(file, content) {
    fs.mkdirSync(path.dirname(file), {recursive: true});
    fs.writeFileSync(file, content);
}

class Remote {
    constructor(root, name, defaultBranch) {
        this.name = name;
        this.defaultBranch = defaultBranch;
        this.bare = path.join(root, 'remotes', `${name}.git`);
        this.work = path.join(root, 'work', name);
        this.url = pathToFileURL(this.bare).href;
        fs.mkdirSync(this.bare, {recursive: true});
        git(this.bare, 'init', '-q', '--bare', '-b', defaultBranch);
        fs.mkdirSync(this.work, {recursive: true});
        git(this.work, 'init', '-q', '-b', defaultBranch);
        git(this.work, 'remote', 'add', 'origin', this.url);
        this.counter = 0;
    }

    // Adds one commit to `branch` (created from the current work HEAD if new) and pushes it.
    commit(branch = this.defaultBranch, files = null) {
        const current = spawnSync('git', ['symbolic-ref', '--short', '-q', 'HEAD'], {
            cwd: this.work,
            env: GIT_ENV,
            encoding: 'utf8'
        }).stdout.trim();
        if (current !== branch) {
            const exists = spawnSync('git', ['rev-parse', '--verify', '-q', `refs/heads/${branch}`], {
                cwd: this.work,
                env: GIT_ENV
            }).status === 0;
            git(this.work, 'checkout', '-q', ...(exists ? [branch] : ['-b', branch]));
        }
        this.counter += 1;
        const content = files ?? {[`${this.name}.txt`]: `${this.name} ${branch} ${this.counter}\n`};
        for (const [file, text] of Object.entries(content)) write(path.join(this.work, file), text);
        git(this.work, 'add', '-A');
        git(this.work, 'commit', '-q', '-m', `${this.name} ${branch} ${this.counter}`);
        git(this.work, 'push', '-q', 'origin', branch);
        return git(this.work, 'rev-parse', 'HEAD');
    }
}

function manifestText(rows) {
    const lines = [
        '# Fixture manifest',
        '# name  path  group  ref  url',
        ...rows.map((r) => [r.name, r.path, r.group, r.ref, r.url].join('    ')),
    ];
    return lines.join('\n') + '\n';
}

// Workspace layout the scenarios start from:
//   ext       external, pinned to its first commit, two commits on main
//   schema    owned, default branch main
//   baseline  owned, default branch develop (proves the script reads the remote's default)
//   lsp       the LSP repo itself: the two scripts plus workspace.deps, default branch master
function makeFixture({rows = null, lspFiles = {}} = {}) {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), 'wsetup-'));
    const fx = {root, remotes: {}};
    fx.ext = new Remote(root, 'ext', 'main');
    fx.extC1 = fx.ext.commit();
    fx.extC2 = fx.ext.commit();
    fx.schema = new Remote(root, 'schema', 'main');
    fx.schema.commit();
    fx.baseline = new Remote(root, 'baseline', 'develop');
    fx.baseline.commit();

    fx.rows = rows ?? [
        {name: 'ext', path: '../ext', group: 'build', ref: fx.extC1, url: fx.ext.url},
        {name: 'eaw-schema', path: 'schema', group: 'build', ref: 'default', url: fx.schema.url},
        {name: 'eaw-baseline', path: 'baseline', group: 'e2e', ref: 'default', url: fx.baseline.url},
    ];

    fx.lspRemote = new Remote(root, 'lsp', 'master');
    const files = {'workspace.deps': manifestText(fx.rows), ...lspFiles};
    for (const f of SCRIPT_FILES) files[f] = fs.readFileSync(path.join(REPO, f), 'utf8');
    fx.lspRemote.commit('master', files);

    fx.ws = path.join(root, 'ws');
    fx.lsp = path.join(fx.ws, 'lsp');
    fs.mkdirSync(fx.ws, {recursive: true});
    git(fx.ws, 'clone', '-q', fx.lspRemote.url, 'lsp');
    fx.dir = (rel) => path.resolve(fx.lsp, rel);
    return fx;
}

function cleanup(fx) {
    try {
        fs.rmSync(fx.root, {recursive: true, force: true, maxRetries: 3});
    } catch {
        // A locked file in the temp folder is not a test failure.
    }
}

function run(shell, fx, options = {}) {
    const [exe, args] = shell.argv(fx, options);
    const r = spawnSync(exe, args, {cwd: fx.lsp, env: GIT_ENV, encoding: 'utf8', timeout: 120000});
    return {status: r.status, stdout: (r.stdout ?? '').replace(/\r/g, ''), stderr: (r.stderr ?? '').replace(/\r/g, '')};
}

function head(dir) {
    return git(dir, 'rev-parse', 'HEAD');
}

function branch(dir) {
    return spawnSync('git', ['symbolic-ref', '--short', '-q', 'HEAD'], {
        cwd: dir,
        env: GIT_ENV,
        encoding: 'utf8'
    }).stdout.trim();
}

function lines(stdout) {
    return stdout.split('\n').filter((l) => l.length > 0);
}

function lineFor(stdout, name) {
    return lines(stdout).find((l) => l.replace(/^\[[a-z]+\] /, '').startsWith(`${name} - `));
}

// SHAs and the temp folder differ between the two fixtures; everything else must match exactly.
function normalize(stdout) {
    return lines(stdout).map((l) => l.replace(/wsetup-[A-Za-z0-9]+/g, 'wsetup-<dir>').replace(/\b[0-9a-f]{7,40}\b/g, '<sha>'));
}

// ── Scenario runner ──────────────────────────────────────────────────────────

function scenario(name, body) {
    test(name, async (t) => {
        const outputs = {};
        for (const shell of SHELLS) {
            await t.test(shell.name, {skip: shell.available ? false : `${shell.name} not found`}, () => {
                const fx = body.setup ? body.setup() : makeFixture();
                try {
                    const result = body.run(shell, fx);
                    outputs[shell.name] = result.stdout;
                } finally {
                    cleanup(fx);
                }
            });
        }
        if (outputs.bash !== undefined && outputs.pwsh !== undefined) {
            assert.deepEqual(normalize(outputs.pwsh), normalize(outputs.bash), 'bash and pwsh printed different status lines');
        }
    });
}

// ── sync: fresh workspace ────────────────────────────────────────────────────

scenario('sync clones the build group: owned on default branch, external at its pin', {
    run(shell, fx) {
        const r = run(shell, fx);
        assert.equal(r.status, 0, r.stdout + r.stderr);
        assert.equal(head(fx.dir('../ext')), fx.extC1);
        assert.equal(branch(fx.dir('../ext')), '', 'a pinned repo is checked out detached');
        assert.equal(branch(fx.dir('schema')), 'main');
        assert.equal(fs.existsSync(fx.dir('baseline')), false, 'e2e is not in the default group');
        assert.match(lineFor(r.stdout, 'ext'), /^\[ok\] ext - Cloned at pin [0-9a-f]{7}$/);
        assert.equal(lineFor(r.stdout, 'eaw-schema'), '[ok] eaw-schema - Cloned on main');
        return r;
    },
});

scenario('sync --groups build,e2e clones on the remote default branch, whatever it is called', {
    run(shell, fx) {
        const r = run(shell, fx, {groups: 'build,e2e'});
        assert.equal(r.status, 0, r.stdout + r.stderr);
        assert.equal(branch(fx.dir('baseline')), 'develop');
        assert.equal(lineFor(r.stdout, 'eaw-baseline'), '[ok] eaw-baseline - Cloned on develop');
        return r;
    },
});

// ── sync: existing repos ─────────────────────────────────────────────────────

scenario('sync fast-forwards an owned repo on its default branch', {
    run(shell, fx) {
        run(shell, fx);
        const tip = fx.schema.commit();
        const r = run(shell, fx);
        assert.equal(r.status, 0, r.stdout + r.stderr);
        assert.equal(head(fx.dir('schema')), tip);
        assert.match(lineFor(r.stdout, 'eaw-schema'), /^\[ok\] eaw-schema - Fast-forwarded main to [0-9a-f]{7}$/);
        assert.match(lineFor(r.stdout, 'ext'), /^\[ok\] ext - At pin [0-9a-f]{7}$/);
        return r;
    },
});

scenario('sync reports an owned repo that is already up to date', {
    run(shell, fx) {
        run(shell, fx);
        const r = run(shell, fx);
        assert.equal(r.status, 0);
        assert.equal(lineFor(r.stdout, 'eaw-schema'), '[ok] eaw-schema - Up to date on main');
        return r;
    },
});

scenario('sync leaves an owned repo on a WIP branch alone', {
    run(shell, fx) {
        run(shell, fx);
        git(fx.dir('schema'), 'checkout', '-q', '-b', 'wip');
        const before = head(fx.dir('schema'));
        fx.schema.commit();
        const r = run(shell, fx);
        assert.equal(r.status, 0, 'branch work is a note, not a failure');
        assert.equal(branch(fx.dir('schema')), 'wip');
        assert.equal(head(fx.dir('schema')), before);
        assert.equal(lineFor(r.stdout, 'eaw-schema'), '[note] eaw-schema - On branch wip, left alone');
        return r;
    },
});

scenario('sync leaves an owned repo with uncommitted changes alone', {
    run(shell, fx) {
        run(shell, fx);
        write(path.join(fx.dir('schema'), 'schema.txt'), 'local edit\n');
        const before = head(fx.dir('schema'));
        fx.schema.commit();
        const r = run(shell, fx);
        assert.equal(r.status, 0);
        assert.equal(head(fx.dir('schema')), before);
        assert.equal(fs.readFileSync(path.join(fx.dir('schema'), 'schema.txt'), 'utf8'), 'local edit\n');
        assert.equal(lineFor(r.stdout, 'eaw-schema'), '[note] eaw-schema - Uncommitted changes, left alone');
        return r;
    },
});

scenario('sync ignores untracked files when deciding whether a repo is clean', {
    run(shell, fx) {
        run(shell, fx);
        write(path.join(fx.dir('schema'), 'scratch.local'), 'untracked\n');
        const tip = fx.schema.commit();
        const r = run(shell, fx);
        assert.equal(r.status, 0);
        assert.equal(head(fx.dir('schema')), tip);
        return r;
    },
});

scenario('sync does not merge an owned default branch that diverged from its remote', {
    run(shell, fx) {
        run(shell, fx);
        write(path.join(fx.dir('schema'), 'local.txt'), 'local commit\n');
        git(fx.dir('schema'), 'add', '-A');
        git(fx.dir('schema'), 'commit', '-q', '-m', 'local');
        const before = head(fx.dir('schema'));
        fx.schema.commit();
        const r = run(shell, fx);
        assert.equal(r.status, 0);
        assert.equal(head(fx.dir('schema')), before);
        assert.equal(lineFor(r.stdout, 'eaw-schema'), '[note] eaw-schema - main diverged from origin/main, not merged');
        return r;
    },
});

scenario('sync moves a clean external repo back to its pin', {
    run(shell, fx) {
        run(shell, fx);
        git(fx.dir('../ext'), 'checkout', '-q', fx.extC2);
        const r = run(shell, fx);
        assert.equal(r.status, 0, r.stdout + r.stderr);
        assert.equal(head(fx.dir('../ext')), fx.extC1);
        assert.match(lineFor(r.stdout, 'ext'), /^\[ok\] ext - Moved to pin [0-9a-f]{7}$/);
        return r;
    },
});

scenario('sync refuses to move an external repo with uncommitted changes, and fails', {
    run(shell, fx) {
        run(shell, fx);
        git(fx.dir('../ext'), 'checkout', '-q', fx.extC2);
        write(path.join(fx.dir('../ext'), 'ext.txt'), 'local edit\n');
        const r = run(shell, fx);
        assert.equal(r.status, 1);
        assert.equal(head(fx.dir('../ext')), fx.extC2);
        assert.match(lineFor(r.stdout, 'ext'), /^\[fail\] ext - Uncommitted changes, not moved to pin [0-9a-f]{7}$/);
        return r;
    },
});

scenario('sync refuses to move an external repo with commits on no remote, and fails', {
    run(shell, fx) {
        run(shell, fx);
        git(fx.dir('../ext'), 'checkout', '-q', '-b', 'local-work');
        write(path.join(fx.dir('../ext'), 'new.txt'), 'x\n');
        git(fx.dir('../ext'), 'add', '-A');
        git(fx.dir('../ext'), 'commit', '-q', '-m', 'unpushed');
        const before = head(fx.dir('../ext'));
        const r = run(shell, fx);
        assert.equal(r.status, 1);
        assert.equal(head(fx.dir('../ext')), before);
        assert.match(lineFor(r.stdout, 'ext'), /^\[fail\] ext - Has commits on no remote, not moved to pin [0-9a-f]{7}$/);
        return r;
    },
});

scenario('sync fails on a pin that does not exist on the remote', {
    setup() {
        const fx = makeFixture();
        return fx;
    },
    run(shell, fx) {
        const bogus = 'f'.repeat(40);
        const rows = fx.rows.map((r) => (r.name === 'ext' ? {...r, ref: bogus} : r));
        const manifest = path.join(fx.root, 'bogus.deps');
        write(manifest, manifestText(rows));
        const r = run(shell, fx, {manifest});
        assert.equal(r.status, 1);
        assert.equal(lineFor(r.stdout, 'ext'), `[fail] ext - Pin fffffff not on ${fx.ext.url}`);
        assert.equal(branch(fx.dir('schema')), 'main', 'the other rows still run');
        return r;
    },
});

scenario('sync finds the remote by URL when origin is a fork', {
    run(shell, fx) {
        run(shell, fx);
        const schema = fx.dir('schema');
        git(schema, 'remote', 'rename', 'origin', 'upstream');
        git(schema, 'remote', 'add', 'origin', pathToFileURL(path.join(fx.root, 'nowhere.git')).href);
        git(schema, 'branch', '-q', '--set-upstream-to=upstream/main');
        const tip = fx.schema.commit();
        const r = run(shell, fx);
        assert.equal(r.status, 0, r.stdout + r.stderr);
        assert.equal(head(schema), tip);
        return r;
    },
});

scenario('sync fails when no remote of an existing repo points at the manifest URL', {
    run(shell, fx) {
        run(shell, fx);
        git(fx.dir('schema'), 'remote', 'set-url', 'origin', pathToFileURL(path.join(fx.root, 'elsewhere.git')).href);
        const r = run(shell, fx);
        assert.equal(r.status, 1);
        assert.equal(lineFor(r.stdout, 'eaw-schema'), `[fail] eaw-schema - No remote points at ${fx.schema.url}`);
        return r;
    },
});

scenario('sync fails on a path that exists but is not a git repository', {
    run(shell, fx) {
        write(path.join(fx.dir('schema'), 'stray.txt'), 'not a repo\n');
        const r = run(shell, fx);
        assert.equal(r.status, 1);
        assert.equal(lineFor(r.stdout, 'eaw-schema'), '[fail] eaw-schema - Exists but is not a git repository');
        return r;
    },
});

// ── Branch matching ──────────────────────────────────────────────────────────

scenario('sync --match-branch clones a matched owned repo on that branch', {
    run(shell, fx) {
        const featTip = fx.schema.commit('features/foo');
        fx.ext.commit('features/foo');
        const r = run(shell, fx, {matchBranch: 'features/foo', groups: 'build,e2e'});
        assert.equal(r.status, 0, r.stdout + r.stderr);
        assert.equal(branch(fx.dir('schema')), 'features/foo');
        assert.equal(head(fx.dir('schema')), featTip);
        assert.equal(head(fx.dir('../ext')), fx.extC1, 'an external repo keeps its pin');
        assert.equal(branch(fx.dir('baseline')), 'develop', 'an unmatched owned repo stays on its default');
        assert.equal(lineFor(r.stdout, 'eaw-schema'), '[ok] eaw-schema - Cloned on features/foo (matched)');
        return r;
    },
});

scenario('sync --match-branch switches a clean existing repo to the matched branch', {
    run(shell, fx) {
        run(shell, fx);
        const featTip = fx.schema.commit('features/foo');
        const r = run(shell, fx, {matchBranch: 'features/foo'});
        assert.equal(r.status, 0, r.stdout + r.stderr);
        assert.equal(branch(fx.dir('schema')), 'features/foo');
        assert.equal(head(fx.dir('schema')), featTip);
        assert.match(lineFor(r.stdout, 'eaw-schema'), /^\[ok\] eaw-schema - Switched to features\/foo \(matched\) at [0-9a-f]{7}$/);
        return r;
    },
});

scenario('sync --match-branch leaves a dirty repo on its branch', {
    run(shell, fx) {
        run(shell, fx);
        fx.schema.commit('features/foo');
        write(path.join(fx.dir('schema'), 'schema.txt'), 'local edit\n');
        const r = run(shell, fx, {matchBranch: 'features/foo'});
        assert.equal(r.status, 0);
        assert.equal(branch(fx.dir('schema')), 'main');
        assert.equal(lineFor(r.stdout, 'eaw-schema'), '[note] eaw-schema - Uncommitted changes, left alone');
        return r;
    },
});

scenario('sync --match-branch with an empty name matches nothing', {
    run(shell, fx) {
        const r = run(shell, fx, {matchBranch: ''});
        assert.equal(r.status, 0, r.stdout + r.stderr);
        assert.equal(branch(fx.dir('schema')), 'main');
        assert.equal(lineFor(r.stdout, 'eaw-schema'), '[ok] eaw-schema - Cloned on main');
        return r;
    },
});

// ── Manifest and argument validation ─────────────────────────────────────────

function badManifest(shell, fx, text) {
    const manifest = path.join(fx.root, 'bad.deps');
    write(manifest, text);
    return run(shell, fx, {manifest});
}

scenario('a row with the wrong column count stops the run with its line number', {
    run(shell, fx) {
        const r = badManifest(shell, fx, '# header\nschema schema build default\n');
        assert.equal(r.status, 2);
        assert.match(r.stderr, /bad\.deps:2 - Expected 5 columns, found 4/);
        assert.equal(fs.existsSync(fx.dir('schema')), false, 'nothing runs on a bad manifest');
        return r;
    },
});

scenario('a path outside the workspace is rejected', {
    run(shell, fx) {
        const r = badManifest(shell, fx, `x ../../x build default ${fx.schema.url}\n`);
        assert.equal(r.status, 2);
        assert.match(r.stderr, /bad\.deps:1 - Path must stay inside the workspace: \.\.\/\.\.\/x/);
        return r;
    },
});

scenario('an absolute path is rejected', {
    run(shell, fx) {
        const r = badManifest(shell, fx, `x /tmp/x build default ${fx.schema.url}\n`);
        assert.equal(r.status, 2);
        assert.match(r.stderr, /bad\.deps:1 - Path must stay inside the workspace: \/tmp\/x/);
        return r;
    },
});

scenario('an unknown group in the manifest is rejected', {
    run(shell, fx) {
        const r = badManifest(shell, fx, `x schema extra default ${fx.schema.url}\n`);
        assert.equal(r.status, 2);
        assert.match(r.stderr, /bad\.deps:1 - Unknown group: extra/);
        return r;
    },
});

scenario('a ref that is neither default nor a full SHA is rejected', {
    run(shell, fx) {
        const r = badManifest(shell, fx, `x schema build main ${fx.schema.url}\n`);
        assert.equal(r.status, 2);
        assert.match(r.stderr, /bad\.deps:1 - Ref must be "default" or a 40-character commit SHA: main/);
        return r;
    },
});

scenario('a duplicate name is rejected', {
    run(shell, fx) {
        const r = badManifest(shell, fx, `x schema build default ${fx.schema.url}\nx baseline build default ${fx.baseline.url}\n`);
        assert.equal(r.status, 2);
        assert.match(r.stderr, /bad\.deps:2 - Duplicate name: x/);
        return r;
    },
});

scenario('an unknown group on the command line is rejected', {
    run(shell, fx) {
        const r = run(shell, fx, {groups: 'build,extra'});
        assert.equal(r.status, 2);
        assert.match(r.stderr, /Unknown group: extra/);
        return r;
    },
});

// ── check ────────────────────────────────────────────────────────────────────

scenario('check passes on a freshly synced workspace', {
    run(shell, fx) {
        run(shell, fx);
        const r = run(shell, fx, {command: 'check'});
        assert.equal(r.status, 0, r.stdout + r.stderr);
        assert.equal(lineFor(r.stdout, 'eaw-schema'), '[ok] eaw-schema - Up to date on main');
        assert.match(lineFor(r.stdout, 'ext'), /^\[ok\] ext - At pin [0-9a-f]{7}$/);
        return r;
    },
});

scenario('check fails when an external repo is not at its pin, and changes nothing', {
    run(shell, fx) {
        run(shell, fx);
        git(fx.dir('../ext'), 'checkout', '-q', fx.extC2);
        const r = run(shell, fx, {command: 'check'});
        assert.equal(r.status, 1);
        assert.equal(head(fx.dir('../ext')), fx.extC2);
        assert.match(lineFor(r.stdout, 'ext'), /^\[fail\] ext - Not at pin [0-9a-f]{7}$/);
        return r;
    },
});

scenario('check notes an owned repo that is behind, and does not fail', {
    run(shell, fx) {
        run(shell, fx);
        const before = head(fx.dir('schema'));
        fx.schema.commit();
        fx.schema.commit();
        const r = run(shell, fx, {command: 'check'});
        assert.equal(r.status, 0);
        assert.equal(head(fx.dir('schema')), before);
        assert.equal(lineFor(r.stdout, 'eaw-schema'), '[note] eaw-schema - Behind origin/main by 2');
        return r;
    },
});

scenario('check fails on a missing repo', {
    run(shell, fx) {
        const r = run(shell, fx, {command: 'check'});
        assert.equal(r.status, 1);
        assert.equal(lineFor(r.stdout, 'eaw-schema'), '[fail] eaw-schema - Missing');
        return r;
    },
});

// ── record ───────────────────────────────────────────────────────────────────

scenario('record writes the current commit of an external repo into the manifest', {
    run(shell, fx) {
        run(shell, fx);
        git(fx.dir('../ext'), 'checkout', '-q', fx.extC2);
        const r = run(shell, fx, {command: 'record'});
        assert.equal(r.status, 0, r.stdout + r.stderr);
        const text = fs.readFileSync(path.join(fx.lsp, 'workspace.deps'), 'utf8');
        assert.ok(text.includes(fx.extC2), 'new pin written');
        assert.ok(!text.includes(fx.extC1), 'old pin replaced');
        assert.ok(text.includes('eaw-schema') && text.includes('default'), 'owned rows untouched');
        assert.match(lineFor(r.stdout, 'ext'), /^\[ok\] ext - Pinned [0-9a-f]{7}, was [0-9a-f]{7}$/);
        assert.equal(lineFor(r.stdout, 'eaw-schema'), undefined, 'record says nothing about owned rows');
        return r;
    },
});

scenario('record refuses a dirty external repo and leaves the manifest untouched', {
    run(shell, fx) {
        run(shell, fx);
        git(fx.dir('../ext'), 'checkout', '-q', fx.extC2);
        write(path.join(fx.dir('../ext'), 'ext.txt'), 'local edit\n');
        const before = fs.readFileSync(path.join(fx.lsp, 'workspace.deps'), 'utf8');
        const r = run(shell, fx, {command: 'record'});
        assert.equal(r.status, 1);
        assert.equal(fs.readFileSync(path.join(fx.lsp, 'workspace.deps'), 'utf8'), before);
        assert.equal(lineFor(r.stdout, 'ext'), '[fail] ext - Uncommitted changes, not recorded');
        return r;
    },
});

scenario('record refuses a commit that is on no remote branch', {
    run(shell, fx) {
        run(shell, fx);
        git(fx.dir('../ext'), 'checkout', '-q', '-b', 'local-work');
        write(path.join(fx.dir('../ext'), 'new.txt'), 'x\n');
        git(fx.dir('../ext'), 'add', '-A');
        git(fx.dir('../ext'), 'commit', '-q', '-m', 'unpushed');
        const before = fs.readFileSync(path.join(fx.lsp, 'workspace.deps'), 'utf8');
        const r = run(shell, fx, {command: 'record'});
        assert.equal(r.status, 1);
        assert.equal(fs.readFileSync(path.join(fx.lsp, 'workspace.deps'), 'utf8'), before);
        assert.match(lineFor(r.stdout, 'ext'), /^\[fail\] ext - [0-9a-f]{7} is on no remote branch, not recorded$/);
        return r;
    },
});

scenario('record reports an unchanged pin', {
    run(shell, fx) {
        run(shell, fx);
        const r = run(shell, fx, {command: 'record'});
        assert.equal(r.status, 0);
        assert.match(lineFor(r.stdout, 'ext'), /^\[ok\] ext - Unchanged at [0-9a-f]{7}$/);
        return r;
    },
});

// ── Self-update ──────────────────────────────────────────────────────────────

scenario('sync fast-forwards the LSP repo first and restarts on the new manifest', {
    run(shell, fx) {
        const extra = new Remote(fx.root, 'extra', 'main');
        extra.commit();
        const rows = [...fx.rows, {name: 'extra', path: 'extra', group: 'build', ref: 'default', url: extra.url}];
        const tip = fx.lspRemote.commit('master', {'workspace.deps': manifestText(rows)});
        const r = run(shell, fx, {noSelfUpdate: false});
        assert.equal(r.status, 0, r.stdout + r.stderr);
        assert.equal(head(fx.lsp), tip);
        assert.equal(branch(fx.dir('extra')), 'main', 'the restarted run read the new manifest');
        assert.match(lines(r.stdout)[0], /^\[ok\] LSP - Fast-forwarded master to [0-9a-f]{7}, restarting$/);
        assert.equal(lines(r.stdout).filter((l) => l.includes(' LSP - ')).length, 1, 'the restarted run does not update again');
        return r;
    },
});

scenario('sync leaves the LSP repo alone on a branch other than its default', {
    run(shell, fx) {
        git(fx.lsp, 'checkout', '-q', '-b', 'features/foo');
        fx.lspRemote.commit('master', {'note.txt': 'x\n'});
        const before = head(fx.lsp);
        const r = run(shell, fx, {noSelfUpdate: false});
        assert.equal(r.status, 0, r.stdout + r.stderr);
        assert.equal(head(fx.lsp), before);
        assert.equal(lineFor(r.stdout, 'LSP'), '[note] LSP - On branch features/foo, not updated');
        return r;
    },
});

scenario('sync reports an LSP repo that is already up to date', {
    run(shell, fx) {
        const r = run(shell, fx, {noSelfUpdate: false});
        assert.equal(r.status, 0, r.stdout + r.stderr);
        assert.equal(lineFor(r.stdout, 'LSP'), '[ok] LSP - Up to date on master');
        return r;
    },
});

// ── Step summary ─────────────────────────────────────────────────────────────

scenario('--summary appends the status lines to the given file', {
    run(shell, fx) {
        const summary = path.join(fx.root, 'summary.md');
        write(summary, 'existing\n');
        const r = run(shell, fx, {summary});
        assert.equal(r.status, 0, r.stdout + r.stderr);
        const text = fs.readFileSync(summary, 'utf8').replace(/\r/g, '');
        assert.ok(text.startsWith('existing\n'), 'appends, never overwrites');
        assert.ok(text.includes('### Workspace'));
        for (const l of lines(r.stdout)) assert.ok(text.includes(l), `summary carries: ${l}`);
        return r;
    },
});
