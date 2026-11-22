// PRV-71 (#441): no frame of the file pose between two takes of an idle. Under the Game renderer the Ewok
// stood in its file pose - arms out - for one frame each time its Idle card drew another take. A clip starts
// from the file pose, and the engine's shaders were handed the bones as they stood in the world before the
// clip's first pose had reached it. Read off every frame drawn: where the bones stand in the world at the
// moment the shaders are handed them.
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { model, openInPreview, openSceneSettings, press } from "./preview.js";
import { pause } from "./particles.js";
import { inNewestWebview } from "./storyGraph.js";

/** What was read off the frames drawn. */
interface Read {
    /** The bones of the model. */
    bones: number;
    /** The takes started. */
    plays: string[];
    /** The frames drawn while a take ran. */
    frames: number;
    /** Of those, the frames in which most bones stood where the file puts them, by the take running. */
    atFilePose: string[];
    /** The most bones at the file's place in any other frame: the ones a take leaves there. */
    restingBones: number;
}

type Recorded = Window & { __prv71?: Read };

async function renderer(id: "game" | "default"): Promise<void> {
    await browser.execute((wanted: string) => {
        const group = Array.from(document.querySelectorAll('[role="radiogroup"]')).find(
            (g) => g.getAttribute("aria-label") === "Which renderer draws the model",
        );
        const option = Array.from(group?.querySelectorAll<HTMLButtonElement>('[role="radio"]') ?? []).find((b) =>
            (b.textContent ?? b.title).trim().toLowerCase().startsWith(wanted),
        );
        option?.click();
    }, id);
}

/** Reads the bones on every frame from now on, against where they stand now: the file pose. Run inside the preview. */
async function read(): Promise<void> {
    await browser.execute(() => {
        type Bone = { matrixWorld: { elements: number[] }; isBone?: boolean };
        const v = (
            window as unknown as {
                aetDebugViewport: {
                    materials: { updateAlamoUniforms: () => void };
                    animation: {
                        play: (name: string | null) => boolean;
                        active: {
                            restPose: Map<Bone, unknown>;
                            action: { getClip(): { name: string } } | null;
                        };
                    };
                };
            }
        ).aetDebugViewport;
        const bones = [...v.animation.active.restPose.keys()].filter((node) => node.isBone === true);
        const file = new Map(bones.map((bone) => [bone, [...bone.matrixWorld.elements]]));
        const seen: Read = { bones: bones.length, plays: [], frames: 0, atFilePose: [], restingBones: 0 };
        (window as Recorded).__prv71 = seen;

        // The engine's shaders take their matrices here, once a frame, before the frame is drawn.
        const hand = v.materials.updateAlamoUniforms.bind(v.materials);
        v.materials.updateAlamoUniforms = () => {
            const action = v.animation.active.action;
            if (action !== null) {
                let resting = 0;
                for (const bone of bones) {
                    const now = bone.matrixWorld.elements;
                    const then = file.get(bone)!;
                    if (now.every((value, i) => Math.abs(value - then[i]) < 1e-5)) {
                        resting++;
                    }
                }
                seen.frames++;
                if (resting * 2 > bones.length) {
                    seen.atFilePose.push(action.getClip().name);
                } else {
                    seen.restingBones = Math.max(seen.restingBones, resting);
                }
            }
            hand();
        };

        const play = v.animation.play.bind(v.animation);
        v.animation.play = (name: string | null) => {
            if (name !== null) {
                seen.plays.push(name);
            }
            return play(name);
        };
    });
}

describe("PRV-71", () => {
    before(async () => {
        await serverReady();
    });

    it("draws no frame of the file pose when the Ewok's idle moves to another take", async () => {
        await openInPreview(model("Ni_ewok.alo"));
        const seen = await inNewestWebview(async () => {
            await browser.waitUntil(
                async () =>
                    browser.execute(() => {
                        const v = (
                            window as unknown as {
                                aetDebugViewport?: { parts: Map<string, unknown>; animationNames: string[] };
                            }
                        ).aetDebugViewport;
                        return (
                            v !== undefined &&
                            v.parts.size > 0 &&
                            v.animationNames.length > 0 &&
                            document.querySelector(".load-cover") === null
                        );
                    }),
                { timeout: 180_000, interval: 500, timeoutMsg: "The Ewok did not finish loading" },
            );

            // The game's own shaders: they are handed their matrices by the preview, a frame at a time.
            await openSceneSettings();
            await renderer("game");
            await browser.waitUntil(
                async () =>
                    browser.execute(
                        () =>
                            (
                                window as unknown as {
                                    aetDebugViewport: { materials: { translated: Map<unknown, unknown> } };
                                }
                            ).aetDebugViewport.materials.translated.size > 0,
                    ),
                { timeout: 120_000, interval: 500, timeoutMsg: "The Ewok was never drawn through the game's shaders" },
            );
            await pause(1_500);
            await read();

            await press('button.rotary-pos[title^="Animation"]');
            await pause(500);
            await press('.anim-card .anim-play[aria-label="Idle"]');
            await pause(25_000);
            return browser.execute(() => (window as Recorded).__prv71!);
        });
        console.log("PRV-71: " + JSON.stringify(seen));

        // Enough takes drawn and frames read to have met the frame after a take starts.
        expect(seen.bones).toBeGreaterThan(10);
        expect(seen.plays.length).toBeGreaterThan(3);
        expect(seen.frames).toBeGreaterThan(500);
        // The takes themselves leave only a few bones where the file has them.
        expect(seen.restingBones * 2).toBeLessThan(seen.bones);

        expect(seen.atFilePose).toEqual([]);
    });
});
