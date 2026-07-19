// Build driver for the extension bundle.
//
// Why this exists instead of a bare `esbuild` CLI call: VS Code's background
// problem matcher (see .vscode/tasks.json) needs deterministic "[watch] build
// started/finished" sentinels to know when a watch build has settled, otherwise
// a watch task used as a preLaunchTask hangs the debugger forever. The plugin
// below emits those sentinels and reformats esbuild errors onto a single,
// matcher-friendly line.

const esbuild = require('esbuild');
const fs = require('fs');
const path = require('path');

const production = process.argv.includes('--production');
const watch = process.argv.includes('--watch');

/**
 * Webview assets referenced at runtime must live under out/ - the VSIX is packaged with
 * `vsce package --no-dependencies`, so anything addressed via node_modules 404s in the
 * published extension (it only works in dev because the folder happens to exist locally).
 */
function copyCodicons() {
  const source = path.join(__dirname, 'node_modules', '@vscode', 'codicons', 'dist');
  const target = path.join(__dirname, 'out', 'codicons');
  fs.mkdirSync(target, { recursive: true });
  for (const file of ['codicon.css', 'codicon.ttf']) {
    fs.copyFileSync(path.join(source, file), path.join(target, file));
  }
}

/**
 * @param {boolean} emitSentinels whether to print the watch sentinels - only the extension
 *   context does, so the problem matcher sees exactly one started/finished pair per build.
 * @returns {import('esbuild').Plugin}
 */
const problemMatcherPlugin = (emitSentinels) => ({
  name: 'esbuild-problem-matcher',
  setup(build) {
    build.onStart(() => { if (emitSentinels) { console.log('[watch] build started'); } });
    build.onEnd((result) => {
      for (const { text, location } of result.errors) {
        if (location) {
          console.error(`    ${location.file}:${location.line}:${location.column}: error: ${text}`);
        } else {
          console.error(`    error: ${text}`);
        }
      }
      if (emitSentinels) { console.log('[watch] build finished'); }
    });
  },
});

async function main() {
  copyCodicons();

  // The webview bundle builds first (and fully, in watch mode too) so the sentinel-emitting
  // extension context always signals "finished" last.
  const webviewCtx = await esbuild.context({
    entryPoints: ['src/webview/storyGraph.tsx'],
    bundle: true,
    format: 'iife',
    platform: 'browser',
    outfile: 'out/webview/storyGraph.js',
    // The auto-arrange plugin imports the node-flavoured elkjs entry (worker files); the
    // bundled build is the self-contained browser variant with the same ELK constructor.
    alias: { elkjs: 'elkjs/lib/elk.bundled.js' },
    define: { 'process.env.NODE_ENV': production ? '"production"' : '"development"' },
    minify: production,
    sourcemap: !production,
    sourcesContent: false,
    logLevel: 'silent',
    plugins: [problemMatcherPlugin(false)],
  });

  const ctx = await esbuild.context({
    entryPoints: ['src/extension.ts'],
    bundle: true,
    format: 'cjs',
    platform: 'node',
    outfile: 'out/extension.js',
    external: ['vscode'],
    minify: production,
    sourcemap: !production,
    sourcesContent: false,
    logLevel: 'silent',
    plugins: [problemMatcherPlugin(true)],
  });

  if (watch) {
    await webviewCtx.watch();
    await ctx.watch();
  } else {
    await webviewCtx.rebuild();
    await webviewCtx.dispose();
    await ctx.rebuild();
    await ctx.dispose();
  }
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
