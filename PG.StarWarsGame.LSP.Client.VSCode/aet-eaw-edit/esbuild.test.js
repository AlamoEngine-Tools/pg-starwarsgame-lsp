// Build driver for the webview unit tests.
//
// Separate from esbuild.js because the two builds have opposite targets: esbuild.js produces the
// browser bundle the webview loads, while these run under Node's built-in test runner. Bundling
// rather than type-stripping keeps the modules under test importable by their source paths, so a
// test reads the same way the webview does.
//
// Only pure modules belong here. Anything importing `vscode` or touching the DOM cannot run under
// this harness - that code is covered by the manual smoke steps instead.

const esbuild = require('esbuild');
const fs = require('fs');
const path = require('path');

const watch = process.argv.includes('--watch');

/** Every *.test.ts under src, so adding a test file needs no build change. */
function findTests(dir) {
    const results = [];
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
        const full = path.join(dir, entry.name);
        if (entry.isDirectory()) {
            results.push(...findTests(full));
        } else if (entry.name.endsWith('.test.ts') || entry.name.endsWith('.test.tsx')) {
            results.push(full);
        }
    }
    return results;
}

async function main() {
    const entryPoints = findTests(path.join(__dirname, 'src'));
    if (entryPoints.length === 0) {
        console.error('No *.test.ts found under src.');
        process.exit(1);
    }

    const options = {
        entryPoints,
        bundle: true,
        format: 'cjs',
        platform: 'node',
        outdir: 'out/test-unit',
        // node: prefixed builtins the tests use; bundling them would break the runner.
        external: ['node:*'],
        sourcemap: true,
        sourcesContent: false,
        logLevel: 'info',
    };

    if (watch) {
        const ctx = await esbuild.context(options);
        await ctx.watch();
    } else {
        await esbuild.build(options);
    }
}

main().catch((e) => {
    console.error(e);
    process.exit(1);
});
