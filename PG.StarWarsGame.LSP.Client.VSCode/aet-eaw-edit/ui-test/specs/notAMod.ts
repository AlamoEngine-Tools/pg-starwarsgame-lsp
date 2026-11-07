// A folder that is not a mod's: the extension activates, and starts no server, offers no project
// file and shows no error. Shared by SET-25 and SET-26.
import { browser, expect } from "@wdio/globals";
import { copyBugReport, lspStatus, notifications, openFile, outputChannels } from "../protocol.js";

export function notMod(title: string, file: string): void {
    describe(title, () => {
        it("activates the extension and starts nothing", async () => {
            await openFile(file);
            await browser.pause(10_000);

            const log = outputChannels().get("EaWEdit") ?? "";
            expect(log).toContain("Extension activated.");
            expect(log).not.toContain("Starting LSP server");
            expect(await lspStatus()).toBeNull();
            expect(
                (await notifications()).filter(
                    (n) => n.message.startsWith("EaWEdit: Mod without a project file") || n.type === "error",
                ),
            ).toEqual([]);
            expect(await copyBugReport()).toContain("- Server: Not running");
        });
    });
}
