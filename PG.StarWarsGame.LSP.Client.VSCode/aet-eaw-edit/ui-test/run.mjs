// Runs protocol tests through the VS Code UI, one fresh VS Code per test, one after another.
//
//   npm run test:ui                   every test with a spec in ui-test/specs
//   npm run test:ui -- PRJ-01 SET-21  those tests
//   npm run test:ui -- --keep-files SET-22
//                                     the test's files as the test before it left them
//
// Each test's files and profile settings come from Start-ProtocolTest.ps1 -DryRun, the same setup
// the protocol's hand runs start from. Local only, never CI: the tests start real servers on the
// maintainer's game folders and take minutes each.
import { spawnSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const launcher = path.resolve(here, "..", "..", "..", "test-protocol", "0.5.0", "Start-ProtocolTest.ps1");
const wdio = path.resolve(here, "..", "node_modules", "@wdio", "cli", "bin", "wdio.js");
const available = fs
    .readdirSync(path.join(here, "specs"))
    .filter((f) => f.endsWith(".spec.ts"))
    .map((f) => f.slice(0, -".spec.ts".length));

const keepFiles = process.argv.includes("--keep-files");
const asked = process.argv
    .slice(2)
    .filter((a) => !a.startsWith("--"))
    .map((a) => a.toUpperCase());
const unknown = asked.filter((a) => !available.includes(a));
if (unknown.length > 0) {
    console.error(`No UI spec for ${unknown.join(", ")}. Available: ${available.join(", ")}`);
    process.exit(2);
}

const results = [];
for (const id of asked.length > 0 ? asked : available) {
    const prepared = spawnSync(
        "pwsh",
        ["-NoProfile", "-File", launcher, id, "-DryRun", ...(keepFiles ? ["-KeepFiles"] : [])],
        { stdio: "inherit" },
    );
    if (prepared.status !== 0) {
        results.push([id, "setup failed"]);
        continue;
    }
    const run = spawnSync(process.execPath, [wdio, "run", path.join(here, "wdio.conf.ts")], {
        stdio: "inherit",
        env: { ...process.env, AET_UI_TEST: id },
        timeout: 15 * 60_000,
    });
    results.push([id, run.status === 0 ? "pass" : run.signal ? `killed (${run.signal})` : "fail"]);
}

console.log("");
for (const [id, outcome] of results) {
    console.log(`${id}  ${outcome}`);
}
process.exit(results.every(([, outcome]) => outcome === "pass") ? 0 : 1);
