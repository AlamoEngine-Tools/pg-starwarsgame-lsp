// PRV-49: an attached hardpoint model sits on the hull bone it is attached to, whatever order the models
// arrive in. On the Gargantuan the front turret model sat inside the hull at the origin, the whole model
// rotated down: since models arrive as bytes, a small turret can land before its hull, and it was then put
// at the model root - with the hull's axis correction already taken off - and never moved when the hull
// arrived.
import { browser, expect } from "@wdio/globals";
import { command, serverReady } from "../protocol.js";
import { closeAll, inNewestWebview } from "./storyGraph.js";

/** Where one attached part sits. */
interface Placement {
    part: string;
    /** The name of the node it hangs on, or "model root". */
    parent: string;
    /** Whether that node is a bone of the hull. */
    onHullBone: boolean;
    /** How far the part's origin is from that node's, in world units. */
    offset: number;
}

describe("PRV-49", () => {
    before(async () => {
        await serverReady();
        await closeAll();
    });

    it("hangs every attached hardpoint model on its hull bone", async () => {
        await command("aet-eaw-edit.lsp.previewAssembledUnit", "Gargantuan_Battle_Platform");
        await inNewestWebview(async () => {
            await browser.waitUntil(
                async () =>
                    browser.execute(() => {
                        // eslint-disable-next-line @typescript-eslint/no-explicit-any
                        const v = (window as any).aetDebugViewport;
                        return (
                            v !== undefined &&
                            v.parts.has("hull") &&
                            [...v.parts.keys()].filter((id: string) => id.startsWith("hp:")).length >= 8
                        );
                    }),
                { timeout: 180_000, interval: 1000, timeoutMsg: "The Gargantuan's parts did not all arrive" },
            );
            await browser.pause(3_000);

            const placements: Placement[] = await browser.execute(() => {
                // eslint-disable-next-line @typescript-eslint/no-explicit-any
                const v = (window as any).aetDebugViewport;
                const hull = v.parts.get("hull");
                const hullBones = new Set(hull.bonesByIndex.values());
                const out: Placement[] = [];
                for (const [id, part] of v.parts) {
                    if (!id.startsWith("hp:")) continue;
                    const parent = part.root.parent;
                    const a = part.root.getWorldPosition(new part.root.position.constructor());
                    const b = parent === null ? a : parent.getWorldPosition(new part.root.position.constructor());
                    out.push({
                        part: id,
                        parent: parent === null ? "none" : parent === v.modelRoot ? "model root" : parent.name,
                        onHullBone: parent !== null && hullBones.has(parent),
                        offset: a.distanceTo(b),
                    });
                }
                return out;
            });
            console.log("PRV-49 placements: " + JSON.stringify(placements));

            expect(placements.length).toBeGreaterThanOrEqual(8);
            for (const p of placements) {
                expect({ part: p.part, onHullBone: p.onHullBone }).toEqual({ part: p.part, onHullBone: true });
                expect(p.offset).toBeLessThan(0.001);
            }
        });
    });
});
