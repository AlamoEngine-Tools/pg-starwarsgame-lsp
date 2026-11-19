// PRV-47 (#445): a clip's visibility track hides the parts on its own bone and nothing below it. The Rebel
// level-2 station's break-up death, Rb_station_02_break_die_00 on Rb_station_02_break.alo, frames 480 to 699
// as measured in the game: `newbreak` and its shadow volume `shadowbreak` are hidden by their own tracks,
// while `Level_02_a`, its shadow volume `level_02sh_a` and `HP02_TBL2_Blast` are drawn. A shadow volume is
// never drawn as a surface; "drawn" means it casts. Before, a hidden bone took its subtree with it, and a
// shadow volume cast whatever its bone's track said.
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { model, openInPreview } from "./preview.js";
import { closeAll, inNewestWebview } from "./storyGraph.js";

/** What one frame shows of the parts the test is about: drawn meshes, and casting shadow volumes. */
interface Frame {
    frame: number;
    drawn: Record<string, boolean>;
    casting: Record<string, boolean>;
}

const MESHES = ["newbreak", "Level_02_a", "HP02_TBL2_Blast"];
const VOLUMES = ["shadowbreak", "level_02sh_a"];

describe("PRV-47", () => {
    before(async () => {
        await serverReady();
        await closeAll();
    });

    it("plays the station's break-up with each part following its own bone's track, frame by frame", async () => {
        await openInPreview(model("Rb_station_02_break.alo"));
        await inNewestWebview(async () => {
            await browser.waitUntil(
                async () =>
                    browser.execute(() => {
                        const v = (window as unknown as { aetDebugViewport?: { parts: Map<string, unknown>; animationNames: string[] } })
                            .aetDebugViewport;
                        return v !== undefined && v.parts.size > 0 && v.animationNames.length > 0;
                    }),
                { timeout: 120_000, timeoutMsg: "The station or its clip did not load" },
            );
            await browser.pause(2_000);

            const frames: Frame[] = await browser.execute(
                (meshes: string[], volumes: string[]) => {
                    // eslint-disable-next-line @typescript-eslint/no-explicit-any
                    const v = (window as unknown as { aetDebugViewport: any }).aetDebugViewport;
                    const clip = "rb_station_02_break_die_00";
                    const name = (v.animationNames as string[]).find((n) => n.toLowerCase() === clip) ?? clip;
                    v.play(name);
                    v.setPaused(true);
                    const fps: number = v.animation.active.visibility.get(name)?.fps ?? 30;

                    // eslint-disable-next-line @typescript-eslint/no-explicit-any
                    const byName = new Map<string, any>();
                    for (const part of v.parts.values()) {
                        // eslint-disable-next-line @typescript-eslint/no-explicit-any
                        part.root.traverse((node: any) => {
                            const mesh = node.userData?.alamo?.alamoMesh;
                            if (node.isMesh === true && typeof mesh === "string" && node.userData.aetShadowVolume !== true) {
                                byName.set(mesh.toLowerCase(), node);
                            }
                        });
                    }
                    const pass = v.environment.shadowVolumes;

                    const out: Frame[] = [];
                    for (let frame = 480; frame <= 699; frame++) {
                        v.seekAnimation(frame / fps);
                        v.rows.resolveRows();
                        const drawn: Record<string, boolean> = {};
                        for (const m of meshes) {
                            const node = byName.get(m.toLowerCase());
                            drawn[m] = node !== undefined && (node.layers.mask & 1) !== 0;
                        }
                        const casting: Record<string, boolean> = {};
                        for (const m of volumes) {
                            const node = byName.get(m.toLowerCase());
                            casting[m] = node !== undefined && pass.bySource.has(node) && !pass.uncounted.has(node);
                        }
                        out.push({ frame, drawn, casting });
                    }
                    return out;
                },
                MESHES,
                VOLUMES,
            );

            const wrong = frames.filter(
                (f) =>
                    f.drawn["newbreak"] !== false ||
                    f.casting["shadowbreak"] !== false ||
                    f.drawn["Level_02_a"] !== true ||
                    f.casting["level_02sh_a"] !== true ||
                    f.drawn["HP02_TBL2_Blast"] !== true,
            );
            console.log(`PRV-47 frames checked: ${frames.length}, wrong: ${wrong.length}`);
            if (wrong.length > 0) {
                console.log("PRV-47 first wrong: " + JSON.stringify(wrong.slice(0, 3)));
            }

            expect(frames).toHaveLength(220);
            expect(wrong).toEqual([]);
        });
    });
});
