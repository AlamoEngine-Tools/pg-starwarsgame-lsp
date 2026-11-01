// One part of a protocol test through the VS Code UI. ui-test/run.mjs starts this once per part
// with AET_UI_TEST and AET_UI_SPEC set, after Start-ProtocolTest.ps1 -DryRun wrote the test's files
// and its profile's settings. Local only: no CI job runs it.
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

// The service writes a profile of its own, from the launcher's settings: created anew for a test's
// first part, kept for the parts after it as a window reload keeps it. A part may say in <part>.json
// beside its spec which folder its window opens - the protocol opening one by hand, or another one -
// and which settings it starts with: the service writes the settings file at every start, so what
// the window before it set is said again there.
const storagePath = path.join("C:\\temp\\aet-vscode", `${id}-ui`);
const spec = process.env.AET_UI_SPEC ?? `${id}.spec.ts`;
if (process.env.AET_UI_FIRST !== "") {
    fs.rmSync(storagePath, { recursive: true, force: true });
}
const partFile = path.join(import.meta.dirname, "specs", spec.replace(/\.spec\.ts$/, ".json"));
const part: { open?: string; settings?: Record<string, unknown> } = fs.existsSync(partFile) ? readJson(partFile) : {};
const open = part.open ?? uiOpen(launch);

export const config: WebdriverIO.Config = {
    runner: "local",
    specs: [`./specs/${spec}`],
    maxInstances: 1,
    capabilities: [
        {
            browserName: "vscode",
            browserVersion: "stable",
            "wdio:vscodeOptions": {
                extensionPath: client,
                // A .code-workspace file opens as a workspace only as a file.
                ...(open === null ? {} : open.endsWith(".code-workspace") ? { filePath: open } : { workspacePath: open }),
                // Traced always: a failure is read from the profile's logs afterwards (see protocol.ts).
                userSettings: {
                    ...readJson(userSettingsFile(id)),
                    "aet-eaw-edit.lsp.debug.trace.server": "verbose",
                    ...part.settings,
                },
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
    // What the window showed when a test failed, beside the profile's logs.
    afterTest: async (test, _context, { passed }) => {
        if (passed) return;
        const dir = path.join(storagePath, "failures");
        fs.mkdirSync(dir, { recursive: true });
        await browser.saveScreenshot(path.join(dir, `${test.title.replace(/[^\w-]+/g, "_")}.png`)).catch(() => undefined);
    },
};
