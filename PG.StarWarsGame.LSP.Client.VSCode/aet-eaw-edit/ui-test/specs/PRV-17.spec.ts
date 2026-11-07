// PRV-17: with no game folder set the assembled frigate still finds the game's textures - all but one emitter's,
// which names its texture by a path on the artist's machine and so names no file; energyPool shows only behind
// @tag:advanced. How the textured hull looks stays a hand check.
import { browser, expect } from "@wdio/globals";
import { closeAll, inNewestWebview } from "./storyGraph.js";
import { command, serverReady, settingsFound, userSettingsJson } from "../protocol.js";
import fs from "node:fs";

describe("PRV-17", () => {
    before(async () => {
        await serverReady();
    });

    it("runs with no game folder setting", () => {
        const settings = JSON.parse(fs.readFileSync(userSettingsJson(), "utf8"));
        expect(settings["aet-eaw-edit.lsp.source.baseGameDirectory"]).toBeUndefined();
        expect(settings["aet-eaw-edit.lsp.source.expansionDirectory"]).toBeUndefined();
    });

    it("assembles the frigate missing only the texture named by a path on the artist's machine", async () => {
        await command("aet-eaw-edit.lsp.previewAssembledUnit", "Nebulon_B_Frigate");
        await inNewestWebview(async () => {
            await (await browser.$(".stage-column")).waitForExist({ timeout: 120_000 });
            await browser.waitUntil(async () => !(await (await browser.$(".load-cover")).isExisting()), {
                timeout: 120_000,
                timeoutMsg: "The frigate never finished loading",
            });
            const tag = await browser.$("button.validate-btn");
            let notes: string[] = [];
            if (await tag.isEnabled()) {
                await browser.execute(() => document.querySelector<HTMLButtonElement>("button.validate-btn")?.click());
                await (await browser.$(".problem-list")).waitForExist({ timeout: 5_000 });
                notes = await browser.execute(() =>
                    Array.from(document.querySelectorAll(".problem-list .problem-msg")).map((m) => (m.textContent ?? "").trim()),
                );
            }
            const missing = notes.filter((n) => /texture/i.test(n));
            expect(missing).toHaveLength(1);
            expect(missing[0].startsWith("Texture 'c:\\art\\textures\\game\\p_particle_master.tga' was not found.")).toBe(true);
        });
        await closeAll();
    });

    it("shows energyPool only behind @tag:advanced, off by default", async () => {
        expect(await settingsFound("energyPool")).toBe(0);
        expect(await settingsFound("@tag:advanced energyPool")).toBe(1);
        const off: boolean | undefined = await browser.executeWorkbench(
            (vscode) => vscode.workspace.getConfiguration().inspect("aet-eaw-edit.features.preview.energyPool")?.defaultValue,
        );
        expect(off).toBe(false);
    });
});
