// PRV-46 (#444): a hardpoint's Damage_Particles bone hides every part on it or below it while the hardpoint
// lives, and shows them all once it is destroyed - the parts the model file hides included. The fixture's unit
// is the Star Destroyer model with two hardpoints of its own: one names HP_F-L_Bone, which carries the
// HP_F-L_Blast mesh and, a level further down, two damage effects; the other names the bone of
// pi_damage_elec_SD00, an effect the file hides. No decal names HP_F-L_Blast. Before, the undamaged hull drew
// HP_F-L_Blast, and destroying the hardpoints lit neither the two effects nor the file-hidden one.
import { browser, expect } from "@wdio/globals";
import { command, serverReady } from "../protocol.js";
import { press } from "./preview.js";
import { inNewestWebview } from "./storyGraph.js";

/** One row of the model tree, as the preview resolved it. */
interface Row {
    id: string;
    parentId: string | null;
    kind: string;
    name: string;
    visible: boolean;
    because?: string;
}

/** The rows the test reads. */
interface Parts {
    blast: Row | undefined;
    effects: Row[];
    fileHidden: Row | undefined;
    control: Row | undefined;
}

/** The rows of the parts the test is about. Run inside the preview. */
async function parts(): Promise<Parts> {
    const rows: Row[] = await browser.execute(() =>
        (window as unknown as { aetDebugViewport: { treeItems(): Row[] } }).aetDebugViewport
            .treeItems()
            .map((r) => ({
                id: r.id,
                parentId: r.parentId,
                kind: r.kind,
                name: r.name,
                visible: r.visible,
                because: r.because,
            })),
    );
    const byName = (name: string): Row | undefined => rows.find((r) => r.name.toLowerCase() === name.toLowerCase());
    const emit = byName("HP_F-L_EmitDamage");

    return {
        blast: byName("HP_F-L_Blast"),
        effects: rows.filter((r) => r.kind === "particle" && emit !== undefined && r.parentId === emit.id),
        fileHidden: rows.find((r) => r.kind === "particle" && /^pi_damage_elec_sd00/i.test(r.name)),
        control: byName("HP_F-R_Blast"),
    };
}

describe("PRV-46", () => {
    before(async () => {
        await serverReady();
    });

    it("hides every part below a living hardpoint's Damage_Particles bone", async () => {
        await command("aet-eaw-edit.lsp.previewAssembledUnit", "Prv_Damage_Unit");
        await inNewestWebview(async () => {
            let seen: Parts | undefined;
            await browser.waitUntil(
                async () => {
                    const v = await browser.execute(
                        () => (window as unknown as { aetDebugViewport?: unknown }).aetDebugViewport !== undefined,
                    );
                    if (!v) return false;
                    seen = await parts();
                    return (
                        seen.blast !== undefined &&
                        seen.effects.length === 2 &&
                        seen.fileHidden !== undefined &&
                        seen.control !== undefined
                    );
                },
                { timeout: 180_000, interval: 1000, timeoutMsg: "The unit's rows did not all arrive" },
            );
            await browser.pause(2_000);
            seen = await parts();
            console.log("PRV-46 undamaged: " + JSON.stringify(seen));

            expect(seen.blast?.visible).toBe(false);
            for (const effect of seen.effects) expect(effect.visible).toBe(false);
            expect(seen.fileHidden?.visible).toBe(false);
            // A mesh below no Damage_Particles bone is left as the file draws it.
            expect(seen.control?.visible).toBe(true);
        });
    });

    it("shows every one of them once the hardpoint is destroyed, the file-hidden effect included", async () => {
        await inNewestWebview(async () => {
            await press('button.rotary-pos[title^="Gameplay"]');
            await press('button[title="Destroy all hardpoints"]');
            let seen: Parts | undefined;
            await browser.waitUntil(
                async () => {
                    seen = await parts();
                    return seen.blast?.visible === true;
                },
                { timeout: 30_000, interval: 500, timeoutMsg: "HP_F-L_Blast was not shown on destruction" },
            ).catch(() => undefined);
            seen = await parts();
            console.log("PRV-46 destroyed: " + JSON.stringify(seen));

            expect(seen.blast?.visible).toBe(true);
            for (const effect of seen.effects) expect(effect.visible).toBe(true);
            expect(seen.fileHidden?.visible).toBe(true);
            expect(seen.control?.visible).toBe(true);
        });
    });
});
