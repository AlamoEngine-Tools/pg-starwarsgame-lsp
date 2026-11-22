// PRV-72: a model a hardpoint attaches is evaluated as a model of its own. A mod's star destroyer lost its two
// bow weapons: each is an attached model whose one drawn mesh is named like the hardpoint's damage decal on the
// hull, and the decal rule - hidden while the hardpoint lives - reached inside and hid the weapon. The fixture's
// corvette attaches a second corvette and names `Corvette` as the decal: a mesh both files carry and draw. The
// unit's own is the decal; the attached one is drawn as its file has it, and nothing shows what that file hides.
import { browser, expect } from "@wdio/globals";
import { command, serverReady } from "../protocol.js";
import { press } from "./preview.js";
import { pause } from "./particles.js";
import { inNewestWebview } from "./storyGraph.js";

/** The meshes of the unit's own model and of the model its hardpoint attaches, by name, and whether each is drawn. */
interface Drawn {
    hull: Record<string, boolean>;
    attached: Record<string, boolean>;
    /** Whether the attached model's part is on screen at all. */
    attachedShown: boolean;
}

/** Reads what is drawn. Run inside the preview. */
async function drawn(): Promise<Drawn> {
    return browser.execute(() => {
        interface Node {
            visible: boolean;
            isMesh?: boolean;
            parent: Node | null;
            children: Node[];
            userData: { alamo?: { alamoMesh?: string } };
        }
        const v = (
            window as unknown as {
                aetDebugViewport: {
                    parts: Map<string, { root: Node; attached?: boolean }>;
                    levels: { drawn(mesh: Node): boolean };
                };
            }
        ).aetDebugViewport;
        const roots = new Set([...v.parts.values()].map((part) => part.root));
        const out: Drawn = { hull: {}, attached: {}, attachedShown: false };

        for (const part of v.parts.values()) {
            const into = part.attached === true ? out.attached : out.hull;
            if (part.attached === true) {
                let shown = true;
                for (let at: Node | null = part.root; at !== null; at = at.parent) {
                    shown = shown && at.visible;
                }
                out.attachedShown = shown;
            }

            // This part's own meshes: another part hung inside it answers for itself.
            const walk = (node: Node): void => {
                if (node !== part.root && roots.has(node)) {
                    return;
                }
                const name = node.userData.alamo?.alamoMesh;
                if (node.isMesh === true && name !== undefined) {
                    const key = name.toLowerCase();
                    into[key] = (into[key] ?? false) || v.levels.drawn(node);
                }
                node.children.forEach(walk);
            };
            walk(part.root);
        }
        return out;
    });
}

describe("PRV-72", () => {
    before(async () => {
        await serverReady();
    });

    it("hides the unit's own decal and draws the attached model as its file has it", async () => {
        await command("aet-eaw-edit.lsp.previewAssembledUnit", "Prv_Attached_Unit");
        await inNewestWebview(async () => {
            let quiet = 0;
            await browser.waitUntil(
                async () => {
                    const done: boolean = await browser.execute(() => {
                        const v = (window as unknown as { aetDebugViewport?: { parts: Map<string, unknown> } })
                            .aetDebugViewport;
                        return v !== undefined && v.parts.size > 1 && document.querySelector(".load-cover") === null;
                    });
                    quiet = done ? quiet + 1 : 0;
                    return quiet >= 3;
                },
                { timeout: 180_000, interval: 1000, timeoutMsg: "The unit and its attached model did not finish loading" },
            );
            await pause(1_000);
            const alive = await drawn();
            console.log("PRV-72 alive: " + JSON.stringify(alive));

            // The unit's own Corvette mesh is the decal: hidden while the hardpoint lives.
            expect(alive.hull.corvette).toBe(false);

            // The attached corvette is a model of its own: its Corvette mesh is no decal, and what its file
            // hides - its collision hull, its engine glow - stays hidden.
            expect(alive.attachedShown).toBe(true);
            expect(alive.attached.corvette).toBe(true);
            expect(alive.attached.collision).toBe(false);
            expect(alive.attached.engines).toBe(false);
        });
    });

    it("shows the decal and takes the attached model away when the hardpoint is destroyed", async () => {
        await inNewestWebview(async () => {
            await press('button.rotary-pos[title^="Gameplay"]');
            await pause(1_500);
            await press('button[title="Destroy all hardpoints"]');
            await pause(1_500);
            const dead = await drawn();
            console.log("PRV-72 destroyed: " + JSON.stringify(dead));

            expect(dead.attachedShown).toBe(false);
            // Nothing the death writes reaches inside the attached model: what its file hides stays hidden.
            expect(dead.attached.collision).toBe(false);
        });
    });
});
