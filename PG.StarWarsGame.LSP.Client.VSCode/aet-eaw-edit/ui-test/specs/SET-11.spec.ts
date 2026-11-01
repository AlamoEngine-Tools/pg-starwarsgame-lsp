// SET-11: unset game directories are found in the Steam library or the registry; a set one wins,
// after a restart only.
import { browser, expect } from "@wdio/globals";
import { extensionLog, restartServer, serverReady, serverStarts, setSettings } from "../protocol.js";

const steam = "G:\\Games\\SteamLibrary\\steamapps\\common\\Star Wars Empire at War";
const fake = "C:\\temp\\aet-test-SET-11\\FakeEaW";

function lastLine(prefix: string): string {
    return (
        extensionLog()
            .split(/\r?\n/)
            .filter((l) => l.includes(prefix))
            .at(-1) ?? ""
    );
}

describe("SET-11", () => {
    it("finds both installs with no directory set", async () => {
        await serverReady();
        expect(lastLine("Game installs detected")).toContain(
            `Game installs detected (base game=${steam}\\GameData, expansion=${steam}\\corruption)`,
        );
        expect(lastLine("LSP configuration loaded")).toContain(
            `LSP configuration loaded (locale=en, gamePath=${steam}\\GameData)`,
        );
    });

    it("does not restart on the setting alone", async () => {
        const starts = serverStarts();
        await setSettings({ "aet-eaw-edit.lsp.source.baseGameDirectory": fake });
        await browser.pause(10_000);
        expect(serverStarts()).toBe(starts);
    });

    it("takes the set directory after a restart", async () => {
        await restartServer();
        expect(lastLine("LSP configuration loaded")).toContain(`LSP configuration loaded (locale=en, gamePath=${fake})`);
        expect(lastLine("Game installs detected")).toContain(
            `Game installs detected (base game=${fake}, expansion=${steam}\\corruption)`,
        );
    });

    after(async () => {
        await setSettings({ "aet-eaw-edit.lsp.source.baseGameDirectory": undefined });
    });
});
