// One protocol test through the VS Code UI. ui-test/run.mjs starts this once per test with
// AET_UI_TEST set, after Start-ProtocolTest.ps1 -DryRun wrote the test's files and its profile's
// settings. Local only: no CI job runs it.
import fs from "node:fs";
import path from "node:path";
import { client, protocolDir, readJson, repo, uiOpen, userSettingsFile } from "./paths.js";

const id = process.env.AET_UI_TEST;
if (!id) {
    throw new Error("AET_UI_TEST is not set - start the tests with npm run test:ui.");
}

const launch = readJson(path.join(protocolDir, "tests.launch.json"))[id];
if (!launch) {
    throw new Error(`No test ${id} in tests.launch.json.`);
}

// The service writes a profile of its own, from the launcher's settings; created anew every run.
const storagePath = path.join("C:\\temp\\aet-vscode", `${id}-ui`);
fs.rmSync(storagePath, { recursive: true, force: true });
const open = uiOpen(id, launch);

export const config: WebdriverIO.Config = {
    runner: "local",
    specs: [`./specs/${id}.spec.ts`],
    maxInstances: 1,
    capabilities: [
        {
            browserName: "vscode",
            browserVersion: "stable",
            "wdio:vscodeOptions": {
                extensionPath: client,
                ...(open === null ? {} : { workspacePath: open }),
                // Traced always: a failure is read from the profile's logs afterwards (see protocol.ts).
                userSettings: { ...readJson(userSettingsFile(id)), "aet-eaw-edit.lsp.debug.trace.server": "verbose" },
                storagePath,
                verboseLogging: false,
            },
        },
    ],
    services: [["vscode", { cachePath: path.join(repo, ".wdio-vscode-service") }]],
    framework: "mocha",
    reporters: ["spec"],
    mochaOpts: { ui: "bdd", timeout: 300_000 },
    logLevel: "warn",
    waitforTimeout: 60_000,
};
