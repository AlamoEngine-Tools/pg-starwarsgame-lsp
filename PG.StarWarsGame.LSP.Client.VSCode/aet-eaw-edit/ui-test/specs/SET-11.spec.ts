// SET-11: unset game directories are found in the Steam library or the registry; a set one wins, and
// setting it restarts the server, which reads it only when it starts (#395).
import { expect } from "@wdio/globals";
import { extensionLog, serverReady, setSettings, setStartupSettings } from "../protocol.js";

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

    it("restarts on the setting and takes the set directory", async () => {
        await setStartupSettings({ "aet-eaw-edit.lsp.source.baseGameDirectory": fake });
        expect(lastLine("LSP configuration loaded")).toContain(`LSP configuration loaded (locale=en, gamePath=${fake})`);
        expect(lastLine("Game installs detected")).toContain(
            `Game installs detected (base game=${fake}, expansion=${steam}\\corruption)`,
        );
    });

    after(async () => {
        await setSettings({ "aet-eaw-edit.lsp.source.baseGameDirectory": undefined });
    });
});
