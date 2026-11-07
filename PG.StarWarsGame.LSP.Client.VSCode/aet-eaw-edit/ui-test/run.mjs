// Runs protocol tests through the VS Code UI, one fresh VS Code per test, one after another.
//
//   npm run test:ui                   every test with a spec in ui-test/specs
//   npm run test:ui -- PRJ-01 SET-21  those tests
//   npm run test:ui -- --keep-files SET-22
//                                     the test's files as the test before it left them
//
// Each test's files and profile settings come from Start-ProtocolTest.ps1 -DryRun, the same setup
// the protocol's hand runs start from. A test the protocol reloads the window in is written in
// parts - <ID>.spec.ts, then <ID>.2.spec.ts and on - each a new VS Code on the same profile, which
// is what a reload is: a session does not survive one. Local only, never CI: the tests start real
// servers on the maintainer's game folders and take minutes each.
//
// What a part does NOT inherit: an extension's globalState. Under the driver VS Code writes no state
// database at all (measured: none in the profile, quit gracefully or not), so a test of something
// kept across a reload checks the same rule inside one window instead.
import { spawnSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const launcher = path.resolve(here, "..", "..", "..", "test-protocol", "0.5.0", "Start-ProtocolTest.ps1");
const wdio = path.resolve(here, "..", "node_modules", "@wdio", "cli", "bin", "wdio.js");
const specs = fs.readdirSync(path.join(here, "specs")).filter((f) => f.endsWith(".spec.ts"));
const partsOf = (id) =>
    specs
        .filter((f) => f === `${id}.spec.ts` || (f.startsWith(`${id}.`) && /^\d+$/.test(f.slice(id.length + 1, -8))))
        .sort((a, b) => a.length - b.length || a.localeCompare(b));
const available = [...new Set(specs.map((f) => f.split(".")[0]))].sort();

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
    let outcome = "pass";
    for (const [i, part] of partsOf(id).entries()) {
        const run = spawnSync(process.execPath, [wdio, "run", path.join(here, "wdio.conf.ts")], {
            stdio: "inherit",
            env: { ...process.env, AET_UI_TEST: id, AET_UI_SPEC: part, AET_UI_FIRST: i === 0 ? "1" : "" },
            timeout: 15 * 60_000,
        });
        if (run.status !== 0) {
            outcome = (run.signal ? `killed (${run.signal})` : "fail") + (i > 0 ? ` in ${part}` : "");
            break;
        }
    }
    results.push([id, outcome]);
}

console.log("");
for (const [id, outcome] of results) {
    console.log(`${id}  ${outcome}`);
}
process.exit(results.every(([, outcome]) => outcome === "pass") ? 0 : 1);
