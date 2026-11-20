// PRV-51 (#451): a hit that takes the hull to or below 75 % or 50 % adds the type's Damaged_Smoke_Asset_Name on
// the model's root bone; one hit through both lines adds one; the killing hit adds none, and the smokes go with
// the unit when it dies. The Snowspeeder names P_Smoke_Small02.alo and carries no hardpoints, so it dies by its
// own hull. Before, no smoke was ever added - and once it was, nothing took it away at death.
import { browser, expect } from "@wdio/globals";
import { command, serverReady } from "../protocol.js";
import { press } from "./preview.js";
import { closeAll, inNewestWebview } from "./storyGraph.js";

/** The damage smokes burning on stage, with the bone each rides. */
interface Smoke {
    id: string;
    boneIndex: number | null;
    part: string | null;
}

/** The smokes on stage. Run inside the preview. */
async function smokes(): Promise<Smoke[]> {
    return browser.execute(() => {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const v = (window as unknown as { aetDebugViewport: any }).aetDebugViewport;
        return (v.particleSystemIds() as string[])
            .filter((id) => id.startsWith("damagedSmoke:"))
            .map((id) => {
                const entry = v.particles.particleSystems.get(id);
                return {
                    id,
                    boneIndex: entry?.attachBoneIndex ?? null,
                    part: entry?.attachToPartId ?? null,
                };
            });
    });
}

/** The armour factor the attacker's damage type takes against the unit, off the Damage type field. */
async function factor(): Promise<number> {
    const text: string = await browser.execute(() => {
        const field = Array.from(document.querySelectorAll(".field")).find((f) =>
            Array.from(f.querySelector(".field-label")?.childNodes ?? []).some(
                (n) => n.nodeType === 3 && (n.textContent ?? "").trim() === "Damage type",
            ),
        );
        return field?.querySelector(".section-count")?.textContent ?? "";
    });
    const value = Number(text.replace(/^x/i, ""));
    if (!(value > 0)) throw new Error(`No armour factor in '${text}'`);
    return value;
}

/** Sets the attacker's Damage the way typing does, so React sees the change. */
async function setDamage(amount: number): Promise<void> {
    await browser.execute((to: number) => {
        const field = Array.from(document.querySelectorAll(".field")).find((f) =>
            Array.from(f.querySelector(".field-label")?.childNodes ?? []).some(
                (n) => n.nodeType === 3 && (n.textContent ?? "").trim() === "Damage",
            ),
        );
        const input = field?.querySelector<HTMLInputElement>('input[type="number"]');
        if (!input) throw new Error("No Damage field");
        const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!;
        setter.call(input, String(to));
        input.dispatchEvent(new Event("input", { bubbles: true }));
    }, amount);
}

/** Fires one shot that takes `hull` hull points, and waits for whatever it adds to arrive. */
async function hit(hull: number, f: number): Promise<void> {
    await setDamage(hull / f);
    await browser.pause(300);
    await press('button[title="Fire at the whole unit"]');
    // The smoke's system is fetched on its first use; give it time to land.
    await browser.pause(3_000);
}

const MAX = 190;

describe("PRV-51", () => {
    before(async () => {
        await serverReady();
        await closeAll();
    });

    it("adds one smoke on the root bone at 75 %, a second at 50 %, none for the killing hit", async () => {
        await command("aet-eaw-edit.lsp.previewAssembledUnit", "Snowspeeder");
        await inNewestWebview(async () => {
            await browser.waitUntil(
                async () =>
                    browser.execute(() => {
                        const v = (window as unknown as { aetDebugViewport?: { parts: Map<string, unknown> } })
                            .aetDebugViewport;
                        return v !== undefined && v.parts.size > 0;
                    }),
                { timeout: 180_000, interval: 1000, timeoutMsg: "The Snowspeeder did not load" },
            );
            await browser.pause(2_000);
            await press('button.rotary-pos[title^="Gameplay"]');
            await press('button[title="Weapon settings"]');
            await browser.pause(1_000);
            const f = await factor();

            await hit(MAX * 0.2, f); // to 80 %
            const atEighty = await smokes();
            await hit(MAX * 0.1, f); // to 70 %
            const atSeventy = await smokes();
            await hit(MAX * 0.25, f); // to 45 %
            const atFortyFive = await smokes();
            console.log("PRV-51 hits: " + JSON.stringify({ f, atEighty, atSeventy, atFortyFive }));

            expect(atEighty).toHaveLength(0);
            expect(atSeventy).toHaveLength(1);
            expect(atSeventy[0].boneIndex).toBe(0);
            expect(atSeventy[0].part).toBe("hull");
            expect(atFortyFive).toHaveLength(2);

            await hit(MAX, f); // the killing hit
            const dead = await smokes();
            console.log("PRV-51 dead: " + JSON.stringify(dead));

            expect(dead).toHaveLength(0);
        });
    });

    it("adds one smoke for a hit that takes the revived unit through both lines", async () => {
        await inNewestWebview(async () => {
            await press('button[title="Repair the target"]');
            await browser.pause(2_000);
            const revived = await smokes();
            const f = await factor();

            await hit(MAX * 0.6, f); // from 100 % to 40 %
            const through = await smokes();
            console.log("PRV-51 through both: " + JSON.stringify({ revived, through }));

            expect(revived).toHaveLength(0);
            expect(through).toHaveLength(1);
        });
    });
});
