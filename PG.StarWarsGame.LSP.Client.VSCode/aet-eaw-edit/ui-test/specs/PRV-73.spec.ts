// PRV-73: a ship's engine glow goes out when its engines go offline. Destroying every engine hardpoint of a
// unit left its glow burning. In the game an engine hardpoint's death takes the engines offline when none is
// left undestroyed, and the space locomotor then keeps the engine effects hidden; the hardpoint's
// `Engine_Particles` plays no part - on the Star Destroyer the glow is not below that bone. Its one engine
// hardpoint destroyed puts the glow out, a weapon destroyed does not, and repairing the engine lights it again.
import { browser, expect } from "@wdio/globals";
import { command, serverReady } from "../protocol.js";
import { press } from "./preview.js";
import { pause } from "./particles.js";
import { inNewestWebview } from "./storyGraph.js";

const ENGINE = "HP_Star_Destroyer_Engines";
const WEAPON = "HP_Star_Destroyer_Weapon_FL";

/** The unit's engine effects - the systems the game groups as `pe` - by whether each is drawn. Run inside the preview. */
async function engineGlow(): Promise<boolean[]> {
    return browser.execute(() => {
        const rig = (
            window as unknown as {
                aetDebugViewport: {
                    particles: {
                        particleSystems: Map<
                            string,
                            { origin: string; system: { name: string }; instance: { root: { visible: boolean } } }
                        >;
                    };
                };
            }
        ).aetDebugViewport.particles;
        return [...rig.particleSystems.values()]
            .filter((entry) => entry.origin === "model" && entry.system.name.toLowerCase().startsWith("pe"))
            .map((entry) => entry.instance.root.visible);
    });
}

/** Presses the destroy or repair button of one hardpoint's card. Run inside the preview. */
async function toggle(hardpoint: string): Promise<void> {
    const pressed: boolean = await browser.execute((id: string) => {
        let node: Element | null = document.querySelector(`[title="Go to where ${id} is defined"]`);
        while (node !== null) {
            const button = node.querySelector<HTMLButtonElement>(
                'button[title="Destroy hardpoint"], button[title="Repair hardpoint"]',
            );
            if (button !== null) {
                button.click();
                return true;
            }
            node = node.parentElement;
        }
        return false;
    }, hardpoint);
    expect(pressed).toBe(true);
    await pause(1_000);
}

describe("PRV-73", () => {
    before(async () => {
        await serverReady();
    });

    it("puts the engine glow out with the last engine hardpoint, and lights it again on repair", async () => {
        await command("aet-eaw-edit.lsp.previewAssembledUnit", "Star_Destroyer");
        await inNewestWebview(async () => {
            let quiet = 0;
            await browser.waitUntil(
                async () => {
                    const done: boolean = await browser.execute(() => {
                        const v = (window as unknown as { aetDebugViewport?: { parts: Map<string, unknown> } })
                            .aetDebugViewport;
                        return v !== undefined && v.parts.size > 0 && document.querySelector(".load-cover") === null;
                    });
                    quiet = done ? quiet + 1 : 0;
                    return quiet >= 3;
                },
                { timeout: 180_000, interval: 1000, timeoutMsg: "The Star Destroyer did not finish loading" },
            );
            await press('button.rotary-pos[title^="Gameplay"]');
            await pause(2_000);

            const intact = await engineGlow();
            await toggle(WEAPON);
            const weaponGone = await engineGlow();
            await toggle(ENGINE);
            const engineGone = await engineGlow();
            await toggle(ENGINE);
            const repaired = await engineGlow();
            console.log("PRV-73: " + JSON.stringify({ intact, weaponGone, engineGone, repaired }));

            expect(intact.length).toBeGreaterThan(0);
            expect(intact.every((drawn) => drawn)).toBe(true);
            // A weapon is not an engine: the engines stay online.
            expect(weaponGone.every((drawn) => drawn)).toBe(true);
            // The one engine hardpoint gone: none left, the engines offline, the glow out.
            expect(engineGone.some((drawn) => drawn)).toBe(false);
            expect(repaired.every((drawn) => drawn)).toBe(true);
        });
    });
});
