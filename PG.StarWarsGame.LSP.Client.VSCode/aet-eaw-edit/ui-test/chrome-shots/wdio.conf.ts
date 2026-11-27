// Takes pictures of the editors' shared chrome - the page toolbar, the dock's header, content and foot -
// through the VS Code UI, in the dark and the light theme. Local only, like the protocol tests. A change
// to the chrome reaches four pages at once, so each chunk of one is looked at here before and after.
//
//   npm run screenshots:chrome                     every page, into ui-test/chrome-shots/out/current
//   AET_SHOT_LABEL=before npm run screenshots:chrome
//                                                  the folder under out/, to keep two states apart
//   AET_SHOT=story npm run screenshots:chrome      one page: story, text, encyclopedia or preview
//
// The pictures are for the eye; nothing here compares them. They are not tracked.
import fs from "node:fs";
import path from "node:path";
import { client, repo } from "../paths.js";

const storagePath = path.join("C:\\temp\\aet-vscode", "chrome-shots-ui");
fs.rmSync(storagePath, { recursive: true, force: true });
fs.mkdirSync(storagePath, { recursive: true });

// A workspace file for its window title: the window sizer finds the window by it, and "eaw" alone could
// be any window.
const workspace = path.join(storagePath, "Chrome Shots.code-workspace");
fs.writeFileSync(workspace, JSON.stringify({ folders: [{ path: path.join(repo, "eaw") }] }, null, 2));

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
                    // The picture is the page: no title bar of the editor's own, no AI chrome.
                    "window.titleBarStyle": "native",
                    "window.customTitleBarVisibility": "never",
                    "window.commandCenter": false,
                    "window.menuBarVisibility": "hidden",
                    "chat.disableAIFeatures": true,
                    "workbench.colorTheme": "Default Dark Modern",
                    "aet-eaw-edit.whatsNew.openAfterUpdate": false,
                    // Every mode of the story graph's dial, so each state of its dock can be reached.
                    "aet-eaw-edit.experimental.storyEditing": true,
                    "aet-eaw-edit.experimental.storySimulator": true,
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
