// Reads, checks and sets the release version in the three files that carry it:
// Directory.Build.props (server), the extension's package.json, and REQUIRED_SERVER_VERSION in
// extension.ts (the server version the extension insists on).
//
//   node scripts/release-version.js check [<version>] [--root <dir>]  prints the version; exit 1 on disagreement
//   node scripts/release-version.js set <version> [--root <dir>]      writes it into all three
//
// Used by the prepare-release and release workflows, and by release.ps1 for local builds.

'use strict';

const fs = require('node:fs');
const path = require('node:path');

const FILES = {
    props: 'Directory.Build.props',
    pkg: 'PG.StarWarsGame.LSP.Client.VSCode/aet-eaw-edit/package.json',
    ext: 'PG.StarWarsGame.LSP.Client.VSCode/aet-eaw-edit/src/extension.ts',
};

// Group 1 is everything before the value, group 2 the value. The package.json pattern only
// matches the top-level key (two-space indent), never a dependency's "version".
const PATTERNS = {
    props: /(<Version>)([^<]*)(?=<\/Version>)/,
    pkg: /^(\s{2}"version":\s*")([^"]*)(?=")/m,
    ext: /(const REQUIRED_SERVER_VERSION = ')([^']*)(?=')/,
};

const VERSION = /^\d+\.\d+\.\d+$/;

function readVersions(root) {
    const result = {};
    for (const [key, rel] of Object.entries(FILES)) {
        const match = PATTERNS[key].exec(fs.readFileSync(path.join(root, rel), 'utf8'));
        result[key] = match ? match[2] : null;
    }
    return result;
}

function checkVersion(root, expected) {
    const versions = readVersions(root);
    const want = expected ?? versions.props;
    const wrong = Object.entries(versions).filter(([, v]) => v !== want);
    if (wrong.length > 0) {
        const lines = wrong.map(([key, v]) => `${FILES[key]} - ${v ?? 'no version found'}`);
        throw new Error(`Version mismatch, expected ${want}:\n${lines.join('\n')}`);
    }
    return want;
}

function setVersion(root, version) {
    if (!VERSION.test(version)) {
        throw new Error(`Version must be MAJOR.MINOR.PATCH: '${version}'`);
    }
    const updated = {};
    for (const [key, rel] of Object.entries(FILES)) {
        const text = fs.readFileSync(path.join(root, rel), 'utf8');
        if (!PATTERNS[key].test(text)) throw new Error(`No version marker in ${rel}`);
        updated[rel] = text.replace(PATTERNS[key], `$1${version}`);
    }
    // Written only once every file has its marker, so a failure changes nothing.
    for (const [rel, text] of Object.entries(updated)) fs.writeFileSync(path.join(root, rel), text);
}

function main(argv) {
    const rootIndex = argv.indexOf('--root');
    const root = rootIndex >= 0 ? argv[rootIndex + 1] : path.resolve(__dirname, '..');
    const args = rootIndex >= 0 ? argv.filter((_, i) => i !== rootIndex && i !== rootIndex + 1) : argv;
    const [command, version] = args;
    try {
        if (command === 'check') {
            console.log(checkVersion(root, version));
        } else if (command === 'set' && version !== undefined) {
            setVersion(root, version);
            console.log(version);
        } else {
            console.error('Usage: release-version.js check [<version>] | set <version> [--root <dir>]');
            return 2;
        }
        return 0;
    } catch (e) {
        console.error(e.message);
        return 1;
    }
}

if (require.main === module) process.exit(main(process.argv.slice(2)));

module.exports = {readVersions, checkVersion, setVersion, FILES};
