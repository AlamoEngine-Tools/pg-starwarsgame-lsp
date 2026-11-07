// PRV-37: the AT-AT preview lists all four of its death clones, in file order.
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";

describe("PRV-37", () => {
    before(async () => {
        await serverReady();
    });

    it("lists the four death clones in file order", async () => {
        await browser.executeWorkbench((vscode) => {
            void vscode.commands.executeCommand("aet-eaw-edit.lsp.previewAssembledUnit", "AT_AT_Walker");
        });
        const workbench = await browser.getWorkbench();
        let webview;
        await browser.waitUntil(
            async () => {
                webview = (await workbench.getAllWebviews())[0];
                return webview !== undefined;
            },
            { timeout: 60_000, timeoutMsg: "No preview opened" },
        );
        await webview!.open();
        try {
            const gameplay = await browser.$('button.rotary-pos[title^="Gameplay"]');
            await gameplay.waitForClickable({ timeout: 120_000 });
            await gameplay.click();

            let clones: { name: string; type: string }[] = [];
            await browser.waitUntil(
                async () => {
                    // A card is headed by the damage type and names the clone object under it.
                    clones = await browser.execute(() =>
                        Array.from(document.querySelectorAll(".clone-card")).map((card) => ({
                            name: (card.querySelector(".card-type")?.textContent ?? "").trim(),
                            type: (card.querySelector(".part-name")?.textContent ?? "").trim(),
                        })),
                    );
                    return clones.length >= 4;
                },
                { timeout: 60_000, timeoutMsg: "Fewer than four death clone cards" },
            );
            expect(clones.map((c) => c.name)).toEqual([
                "AT_AT_Walker_Cable_Attack_Death_Clone",
                "AT_AT_Walker_Death_Clone_00",
                "AT_AT_Walker_Death_Clone_01",
                "AT_AT_Walker_Death_Clone_02",
            ]);
            expect(clones.map((c) => c.type)).toEqual([
                "Damage_Cable_Attack",
                "Damage_Normal",
                "Damage_Normal",
                "Damage_Normal",
            ]);
        } finally {
            await webview!.close();
        }
    });
});
