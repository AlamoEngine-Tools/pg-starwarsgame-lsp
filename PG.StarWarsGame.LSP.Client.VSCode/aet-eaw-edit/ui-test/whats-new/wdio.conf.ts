// Takes the What's New page's highlight screenshots through the VS Code UI: one window at 1920x1080 on a
// workspace of the two vanilla projects, the extension under development. Local only, like the protocol
// tests; the frames land in media/whats-new/<version>/ and ship inside the VSIX.
//
//   npm run screenshots:whats-new              every frame
//   AET_SHOT=probe npm run screenshots:whats-new
//                                              lists the files with findings, to pick the editors' files from
//   AET_SHOT_LUA=<file> AET_SHOT_XML=<file> AET_SHOT_NEEDLE=<regex> AET_SHOT_OBJECT=<name>
//                                              what the frames show; see shots.spec.ts for the defaults
//   AET_SHOT_RELEASE=<version>                 the media/whats-new/<version> folder; 0.5.0 unless set
import fs from "node:fs";
import path from "node:path";
import { client, repo } from "../paths.js";

const storagePath = path.join("C:\\temp\\aet-vscode", "whats-new-ui");
fs.rmSync(storagePath, { recursive: true, force: true });
fs.mkdirSync(storagePath, { recursive: true });

// Both vanilla projects in one window: the frame shows two roots, two servers and panels filed per project.
const workspace = path.join(storagePath, "Empire at War.code-workspace");
fs.writeFileSync(
    workspace,
    JSON.stringify({ folders: [{ path: path.join(repo, "eaw") }, { path: path.join(repo, "foc") }] }, null, 2),
);

export const config: WebdriverIO.Config = {
    runner: "local",
    specs: ["./shots.spec.ts"],
    maxInstances: 1,
    capabilities: [
        {
            browserName: "vscode",
            browserVersion: "stable",
            "wdio:vscodeOptions": {
                extensionPath: client,
                filePath: workspace,
                storagePath,
                verboseLogging: false,
                userSettings: {
                    "security.workspace.trust.enabled": false,
                    "workbench.startupEditor": "none",
                    "workbench.tips.enabled": false,
                    "update.mode": "none",
                    "extensions.autoCheckUpdates": false,
                    "telemetry.telemetryLevel": "off",
                    // The frame is the workbench alone: the OS draws the title bar, outside the capture, and
                    // nothing of the editor's own AI chrome is in it.
                    "window.titleBarStyle": "native",
                    "window.customTitleBarVisibility": "never",
                    "window.commandCenter": false,
                    "window.menuBarVisibility": "hidden",
                    "chat.disableAIFeatures": true,
                    "workbench.colorTheme": "Default Dark Modern",
                    "aet-eaw-edit.lsp.schema.source": "local",
                    "aet-eaw-edit.lsp.schema.localPath": path.join(repo, "schema", "eaw"),
                    "aet-eaw-edit.lsp.source.baseline.type": "local",
                    "aet-eaw-edit.lsp.source.baseline.localPath": path.join(repo, "baseline"),
                },
            },
        },
    ],
    services: [["vscode", { cachePath: path.join(repo, ".wdio-vscode-service") }]],
    framework: "mocha",
    reporters: ["spec"],
    mochaOpts: { ui: "bdd", timeout: 900_000 },
    logLevel: "warn",
    waitforTimeout: 60_000,
};
