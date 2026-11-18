// PRV-44 (#437): a turret turned by hand keeps its turn, and its rest, through a clip. On the Gargantuan, a
// main turret is turned in the Model lens, then a clip is played and stopped in the Animation lens. The preview
// re-synced the subject while the clip ran, and the turret layer then took the turned bone as the pose the model
// authored: the turn doubled, and after the clip the turret rested a whole turn off where the model puts it,
// until the preview was closed.
import { browser, expect } from "@wdio/globals";
import { command, serverReady } from "../protocol.js";
import { closeAll, inNewestWebview } from "./storyGraph.js";

const turret = "hardpoint:HP_Gargantuan_Main_Turret_Front";

/** The turret's aim, and how far its yaw bone is turned off the file's pose, in degrees. */
interface Turn {
    aim: number | null;
    offFile: number;
}

async function turn(): Promise<Turn> {
    return browser.execute((id: string) => {
        type Q = { angleTo(q: Q): number };
        type Node = { quaternion: Q };
        const v = (
            window as unknown as {
                aetDebugViewport: {
                    turret: {
                        turretAims: Map<string, { yaw: number }>;
                        turretHandleNodes: Map<string, { yaw: { bone: Node } }>;
                    };
                    animation: { active: { restPose: Map<Node, { quaternion: Q }> } };
                };
            }
        ).aetDebugViewport;
        const bone = v.turret.turretHandleNodes.get(id)!.yaw.bone;
        const file = v.animation.active.restPose.get(bone)!.quaternion;
        return {
            aim: v.turret.turretAims.get(id)?.yaw ?? null,
            offFile: (bone.quaternion.angleTo(file) * 180) / Math.PI,
        };
    }, turret);
}

/** Canvas-centred pixel offsets of the turret's bone or its yaw knob. */
async function spot(kind: "bone" | "knob"): Promise<{ x: number; y: number }> {
    return browser.execute(
        (id: string, k: string) => {
            type V3 = { project(c: unknown): V3; clone(): V3; x: number; y: number };
            type Node = { getWorldPosition(v: V3): V3; position: { constructor: new () => V3 } };
            const v = (
                window as unknown as {
                    aetDebugViewport: {
                        turret: { turretHandleNodes: Map<string, { yaw: { bone: Node; knob: Node } }> };
                        camera: unknown;
                        canvas: HTMLCanvasElement;
                    };
                }
            ).aetDebugViewport;
            const nodes = v.turret.turretHandleNodes.get(id)!.yaw;
            const p = new nodes.knob.position.constructor();
            (k === "knob" ? nodes.knob : nodes.bone).getWorldPosition(p);
            const ndc = p.clone().project(v.camera);
            const rect = v.canvas.getBoundingClientRect();
            return { x: Math.round(ndc.x * 0.5 * rect.width), y: Math.round(-ndc.y * 0.5 * rect.height) };
        },
        turret,
        kind,
    );
}

async function press(selector: string): Promise<void> {
    const element = await browser.$(selector);
    await element.waitForExist({ timeout: 120_000 });
    await browser.execute((e) => (e as unknown as HTMLElement).click(), element);
    await browser.pause(1_000);
}

describe("PRV-44", () => {
    before(async () => {
        await serverReady();
    });

    it("keeps a turned turret's turn and rest through a clip", async () => {
        await command("aet-eaw-edit.lsp.previewAssembledUnit", "Gargantuan_Battle_Platform");
        const seen = await inNewestWebview(async () => {
            await press('button.rotary-pos[title^="Model"]');
            await browser.waitUntil(
                async () =>
                    browser.execute(
                        (id: string) =>
                            (
                                window as unknown as {
                                    aetDebugViewport?: { turret: { turretHandleNodes: Map<string, unknown> } };
                                }
                            ).aetDebugViewport?.turret.turretHandleNodes.has(id) ?? false,
                        turret,
                    ),
                { timeout: 120_000, timeoutMsg: "The turret's handles were not built" },
            );
            await browser.pause(6_000);

            // Point at the turret so its handles draw, then drag its yaw knob.
            const canvas = await browser.$("canvas");
            const bone = await spot("bone");
            await browser.action("pointer").move({ origin: canvas, x: bone.x, y: bone.y }).pause(300).perform();
            await browser.pause(300);
            const knob = await spot("knob");
            await browser
                .action("pointer")
                .move({ origin: canvas, x: knob.x, y: knob.y })
                .pause(200)
                .down()
                .pause(150)
                .move({ origin: canvas, x: knob.x + 30, y: knob.y + 10 })
                .pause(150)
                .up()
                .perform();
            await browser.pause(500);
            const turned = await turn();

            await press('button.rotary-pos[title^="Animation"]');
            await press('.anim-card .anim-play[aria-label="Idle"]');
            await browser.pause(2_500);
            const during = await turn();
            await press(".anim-list button");
            const after = await turn();

            await press('button.rotary-pos[title^="Model"]');
            await press(".subject-state .turrets button");
            const reset = await turn();
            return { turned, during, after, reset };
        });
        console.log(`PRV-44 ${JSON.stringify(seen)}`);

        // The drag turned it; the bone is turned by exactly the aim.
        expect(seen.turned.aim).not.toBeNull();
        expect(Math.abs(seen.turned.aim!)).toBeGreaterThan(5);
        const aim = Math.abs(seen.turned.aim!);
        expect(Math.abs(seen.turned.offFile - aim)).toBeLessThan(0.5);

        // The Idle clip does not key this turret, so the turn is the aim during and after it - not twice the aim.
        expect(Math.abs(seen.during.offFile - aim)).toBeLessThan(0.5);
        expect(Math.abs(seen.after.offFile - aim)).toBeLessThan(0.5);

        // Reset puts it back where the model authored it.
        expect(seen.reset.aim).toBeNull();
        expect(seen.reset.offFile).toBeLessThan(0.5);
    });

    after(async () => {
        await closeAll();
    });
});
