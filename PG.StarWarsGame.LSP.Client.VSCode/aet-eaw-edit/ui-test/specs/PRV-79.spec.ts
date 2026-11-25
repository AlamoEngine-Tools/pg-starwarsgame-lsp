// PRV-79: a stage flyout opens at the pill that opens it (#222). Each of the preview's five - Scene, Camera,
// Abilities, Weapon and Damage log - sits the gap clear of its pill's plate, away from the stage edge the pill
// is on, and centred on the pill unless the stage's side edge stops it first, and it follows the pill when the
// stage is resized. A tail on the flyout's edge points at the pill. Before, each hung from a fixed corner of the
// stage, whatever pill had opened it, and nothing joined the two.
import { browser, expect } from "@wdio/globals";
import { command, serverReady } from "../protocol.js";
import { flyoutPlacement, misplaced, openFlyout, tailOff } from "./flyouts.js";
import { press } from "./preview.js";
import { inNewestWebview } from "./storyGraph.js";

async function lens(name: string): Promise<void> {
    const position = await browser.$(`button.rotary-pos[title^="${name}"]`);
    await position.waitForClickable({ timeout: 120_000 });
    await position.click();
    await browser.pause(1000);
}

describe("PRV-79", () => {
    before(async () => {
        await serverReady();
        await command("aet-eaw-edit.lsp.previewAssembledUnit", "Nebulon_B_Frigate");
    });

    it("opens Scene and Camera below their pills on the top edge", async () => {
        await inNewestWebview(async () => {
            await lens("Model");
            const scene = await openFlyout('button[title="Scene settings"]', "Scene");
            const camera = await openFlyout('button[title="Camera settings"]', "Camera");
            expect([...misplaced("Scene", scene), ...misplaced("Camera", camera)]).toEqual([]);
            expect([...tailOff("Scene", scene), ...tailOff("Camera", camera)]).toEqual([]);
            await browser.keys("Escape");
        });
    });

    it("opens Abilities, Weapon and Damage log above their pills on the bottom edge", async () => {
        await inNewestWebview(async () => {
            await lens("Gameplay");
            const abilities = await openFlyout('button[title="Show abilities"]', "Abilities");
            const weapon = await openFlyout('button[title="Weapon settings"]', "Weapon");
            // The log has nothing to show until something was hit.
            await press('button.rotary-action[title="Destroy all hardpoints"]');
            const log = await openFlyout('button[title="Damage log"]', "Damage log");
            expect([
                ...misplaced("Abilities", abilities),
                ...misplaced("Weapon", weapon),
                ...misplaced("Damage log", log),
            ]).toEqual([]);
            // Weapon and Damage log stop at the same stage edge: the tail is what says which pill each belongs to.
            expect([...tailOff("Abilities", abilities), ...tailOff("Weapon", weapon), ...tailOff("Damage log", log)]).toEqual([]);
        });
    });

    it("follows its pill when the stage gets narrower", async () => {
        await inNewestWebview(async () => {
            const pill = 'button[title="Show abilities"]';
            const before = await openFlyout(pill, "Abilities");
            // A wider dock is a narrower stage, and the command bar moves with the stage's middle.
            await browser.execute(() => {
                const dock = document.querySelector<HTMLElement>(".right-dock");
                if (dock) dock.style.width = `${dock.offsetWidth + 200}px`;
            });
            await browser.waitUntil(async () => (await flyoutPlacement(pill, "Abilities"))?.pill.left !== before.pill.left, {
                timeout: 10_000,
                timeoutMsg: "The stage did not get narrower",
            });
            await browser.pause(300);
            const after = (await flyoutPlacement(pill, "Abilities"))!;
            expect(after.flyout.left).not.toBe(before.flyout.left);
            expect(misplaced("Abilities", after)).toEqual([]);
            expect(tailOff("Abilities", after)).toEqual([]);
        });
    });
});
