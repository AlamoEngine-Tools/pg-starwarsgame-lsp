// Build driver for the extension bundle.
//
// Why this exists instead of a bare `esbuild` CLI call: VS Code's background
// problem matcher (see .vscode/tasks.json) needs deterministic "[watch] build
// started/finished" sentinels to know when a watch build has settled, otherwise
// a watch task used as a preLaunchTask hangs the debugger forever. The plugin
// below emits those sentinels and reformats esbuild errors onto a single,
// matcher-friendly line.

const esbuild = require('esbuild');

const production = process.argv.includes('--production');
const watch = process.argv.includes('--watch');

/** @type {import('esbuild').Plugin} */
const problemMatcherPlugin = {
  name: 'esbuild-problem-matcher',
  setup(build) {
    build.onStart(() => console.log('[watch] build started'));
    build.onEnd((result) => {
      for (const { text, location } of result.errors) {
        if (location) {
          console.error(`    ${location.file}:${location.line}:${location.column}: error: ${text}`);
        } else {
          console.error(`    error: ${text}`);
        }
      }
      console.log('[watch] build finished');
    });
  },
};

async function main() {
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
    plugins: [problemMatcherPlugin],
  });

  if (watch) {
    await ctx.watch();
  } else {
    await ctx.rebuild();
    await ctx.dispose();
  }
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
