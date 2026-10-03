// Tests for scripts/release-version.js. Run: node --test scripts/release-version.test.js

'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const {spawnSync} = require('node:child_process');
const {readVersions, checkVersion, setVersion, FILES} = require('./release-version');

function repo(props = '0.4.0', pkg = '0.4.0', ext = '0.4.0') {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), 'relver-'));
    const write = (rel, text) => {
        fs.mkdirSync(path.dirname(path.join(root, rel)), {recursive: true});
        fs.writeFileSync(path.join(root, rel), text);
    };
    write(FILES.props, `<Project>\n  <PropertyGroup>\n    <Version>${props}</Version>\n  </PropertyGroup>\n</Project>\n`);
    write(FILES.pkg, `{\n  "name": "aet-eaw-edit",\n  "version": "${pkg}",\n  "dependencies": { "x": { "version": "9.9.9" } }\n}\n`);
    write(FILES.ext, `const A = 1;\nconst REQUIRED_SERVER_VERSION = '${ext}';\nconst B = 2;\n`);
    return root;
}

test('readVersions reads all three files', () => {
    assert.deepEqual(readVersions(repo('1.2.3', '1.2.3', '1.2.3')), {props: '1.2.3', pkg: '1.2.3', ext: '1.2.3'});
});

test('checkVersion returns the version when all three agree', () => {
    assert.equal(checkVersion(repo('0.4.1', '0.4.1', '0.4.1')), '0.4.1');
});

test('checkVersion names the file that disagrees', () => {
    assert.throws(() => checkVersion(repo('0.4.1', '0.4.0', '0.4.1')), /package\.json.*0\.4\.0/s);
});

test('checkVersion fails against a different expected version', () => {
    assert.throws(() => checkVersion(repo(), '0.5.0'), /0\.5\.0/);
});

test('setVersion writes all three and leaves everything else alone', () => {
    const root = repo();
    setVersion(root, '0.4.1');
    assert.equal(checkVersion(root), '0.4.1');
    const pkg = fs.readFileSync(path.join(root, FILES.pkg), 'utf8');
    assert.match(pkg, /"version": "9\.9\.9"/, 'nested versions untouched');
    assert.match(fs.readFileSync(path.join(root, FILES.ext), 'utf8'), /const A = 1;\n.*\nconst B = 2;/s);
});

test('setVersion keeps CRLF line endings', () => {
    const root = repo();
    const file = path.join(root, FILES.props);
    fs.writeFileSync(file, fs.readFileSync(file, 'utf8').replace(/\n/g, '\r\n'));
    setVersion(root, '0.4.1');
    assert.match(fs.readFileSync(file, 'utf8'), /<Version>0\.4\.1<\/Version>\r\n/);
});

// The Marketplace has no SemVer pre-release suffixes; a pre-release is a flag, not a version.
for (const bad of ['0.4', '0.4.1-rc.1', 'v0.4.1', '0.4.1.0', '']) {
    test(`setVersion rejects '${bad}'`, () => {
        assert.throws(() => setVersion(repo(), bad), /MAJOR\.MINOR\.PATCH/);
    });
}

test('setVersion fails when a file lacks its version marker', () => {
    const root = repo();
    fs.writeFileSync(path.join(root, FILES.ext), 'nothing here\n');
    assert.throws(() => setVersion(root, '0.4.1'), /extension\.ts/);
});

test('the CLI prints the version for check and exits 1 on a mismatch', () => {
    const script = path.join(__dirname, 'release-version.js');
    const ok = spawnSync(process.execPath, [script, 'check', '--root', repo('0.4.1', '0.4.1', '0.4.1')], {encoding: 'utf8'});
    assert.equal(ok.status, 0);
    assert.equal(ok.stdout.trim(), '0.4.1');
    const bad = spawnSync(process.execPath, [script, 'check', '--root', repo('0.4.1', '0.4.0', '0.4.1')], {encoding: 'utf8'});
    assert.equal(bad.status, 1);
});
