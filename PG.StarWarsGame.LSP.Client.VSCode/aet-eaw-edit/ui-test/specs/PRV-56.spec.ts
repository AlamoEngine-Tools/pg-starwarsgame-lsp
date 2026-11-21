// PRV-56 (#418): the preview filters and mipmaps a TGA texture as the game does, and the Game renderer draws
// the stencil shadow's darken after the opaque meshes and before the transparent ones. The fixture's model is
// the waypoint beacon - a shadow mesh, a hull and two additive rings - with a generated TGA under the name of
// its hull texture. The TGA is sampled LINEAR with a full mip chain in both renderers, and under Game every
// transparent mesh draws after the darken, so glass, engine glow and additive meshes are never darkened.
// Before, the TGA was sampled NEAREST from its top level alone.
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { openInPreview, openSceneSettings, press } from "./preview.js";
import { closeAll, inNewestWebview } from "./storyGraph.js";

const MODEL =
    "C:\\temp\\aet-test-PRV-filter\\Data\\Art\\Models\\Prv_filter.alo";
const TEXTURE = "eb_basewall.tga";
/** The generated texture's edge, in texels. */
const SIZE = 256;

/** Waits for the model's parts to arrive. Run inside the preview. */
async function loaded(): Promise<void> {
    await browser.waitUntil(
        async () =>
            browser.execute(() => {
                const v = (
                    window as unknown as {
                        aetDebugViewport?: { parts: Map<string, unknown> };
                    }
                ).aetDebugViewport;
                return v !== undefined && v.parts.size > 0;
            }),
        { timeout: 120_000, timeoutMsg: "The model did not load" },
    );
    await browser.pause(3_000);
}

/** Picks a position of the Renderer switch, by its label. */
async function pick(label: string): Promise<void> {
    await press(
        `//*[@aria-label='Which renderer draws the model']//button[normalize-space()='${label}']`,
    );
    await browser.pause(2_000);
}

interface Sampling {
    found: boolean;
    minFilter: string;
    magFilter: string;
    /** The mip levels the GPU holds for the texture. */
    levels: number;
}

/** How the GPU samples the generated TGA, read off the GL texture itself. Run inside the preview. */
async function sampling(name: string): Promise<Sampling> {
    return browser.waitUntil(
        async () =>
            browser.execute((wanted: string) => {
                // eslint-disable-next-line @typescript-eslint/no-explicit-any
                const v = (window as unknown as { aetDebugViewport: any })
                    .aetDebugViewport;
                const texture = v.textures.get(wanted);
                const handle =
                    texture === undefined
                        ? undefined
                        : v.renderer.properties.get(texture).__webglTexture;
                if (handle === undefined) {
                    return false;
                }
                const gl = v.renderer.getContext() as WebGL2RenderingContext;
                const names = new Map<number, string>([
                    [gl.NEAREST, "NEAREST"],
                    [gl.LINEAR, "LINEAR"],
                    [gl.NEAREST_MIPMAP_NEAREST, "NEAREST_MIPMAP_NEAREST"],
                    [gl.LINEAR_MIPMAP_NEAREST, "LINEAR_MIPMAP_NEAREST"],
                    [gl.NEAREST_MIPMAP_LINEAR, "NEAREST_MIPMAP_LINEAR"],
                    [gl.LINEAR_MIPMAP_LINEAR, "LINEAR_MIPMAP_LINEAR"],
                ]);
                gl.bindTexture(gl.TEXTURE_2D, handle);
                const result = {
                    found: true,
                    minFilter:
                        names.get(
                            gl.getTexParameter(
                                gl.TEXTURE_2D,
                                gl.TEXTURE_MIN_FILTER,
                            ),
                        ) ?? "?",
                    magFilter:
                        names.get(
                            gl.getTexParameter(
                                gl.TEXTURE_2D,
                                gl.TEXTURE_MAG_FILTER,
                            ),
                        ) ?? "?",
                    levels: gl.getTexParameter(
                        gl.TEXTURE_2D,
                        gl.TEXTURE_IMMUTABLE_LEVELS,
                    ) as number,
                };
                // The bind went past three's state cache; reset it so the next frame binds afresh.
                v.renderer.resetState();
                return result;
            }, name),
        {
            timeout: 60_000,
            timeoutMsg: `The texture '${name}' never reached the GPU`,
        },
    ) as Promise<Sampling>;
}

interface Drawn {
    name: string;
    darken: boolean;
    transparent: boolean;
    shader: string;
}

/** The meshes one frame of the scene draws, in draw order. Run inside the preview. */
async function drawOrder(): Promise<Drawn[]> {
    await browser.execute(() => {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const w = window as any;
        const v = w.aetDebugViewport;
        const renderer = v.renderer;
        const original = renderer.render;
        w.__prv56 = null;
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        renderer.render = function (scene: any, camera: any) {
            if (scene !== v.scene || w.__prv56 !== null) {
                return original.call(this, scene, camera);
            }
            const log: unknown[] = [];
            // eslint-disable-next-line @typescript-eslint/no-explicit-any
            const hooked: [any, any][] = [];
            // eslint-disable-next-line @typescript-eslint/no-explicit-any
            scene.traverse((node: any) => {
                if (node.isMesh !== true) {
                    return;
                }
                const before = node.onBeforeRender;
                hooked.push([node, before]);
                node.onBeforeRender = function (...args: unknown[]) {
                    log.push({
                        name: node.name,
                        darken: node.name === "aetShadowDarken",
                        model:
                            node.userData.alamo !== undefined &&
                            node.userData.aetShadowVolume !== true,
                        transparent: node.material.transparent === true,
                        shader: node.userData.alamo?.alamoShader ?? "",
                    });
                    return before.apply(this, args);
                };
            });
            const out = original.call(this, scene, camera);
            for (const [node, before] of hooked) {
                node.onBeforeRender = before;
            }
            renderer.render = original;
            w.__prv56 = log;
            return out;
        };
    });

    const frame: { drawn: (Drawn & { model: boolean })[] | null } = {
        drawn: null,
    };
    await browser.waitUntil(
        async () =>
            (frame.drawn = await browser.execute(
                () =>
                    (
                        window as unknown as {
                            __prv56: (Drawn & { model: boolean })[] | null;
                        }
                    ).__prv56,
            )) !== null,
        { timeout: 10_000, timeoutMsg: "No frame was drawn" },
    );
    return (frame.drawn ?? []).filter((d) => d.model || d.darken);
}

describe("PRV-56", () => {
    before(async () => {
        await serverReady();
        await closeAll();
    });

    it("samples a TGA texture linearly with a full mip chain in both renderers", async () => {
        await openInPreview(MODEL);
        await inNewestWebview(async () => {
            await loaded();
            await openSceneSettings();
            const levels = Math.floor(Math.log2(SIZE)) + 1;

            await pick("Default");
            const plain = await sampling(TEXTURE);
            console.log("PRV-56 TGA under Default: " + JSON.stringify(plain));
            expect(plain.minFilter).toBe("LINEAR_MIPMAP_LINEAR");
            expect(plain.magFilter).toBe("LINEAR");
            expect(plain.levels).toBe(levels);

            await pick("Game");
            const game = await sampling(TEXTURE);
            console.log("PRV-56 TGA under Game: " + JSON.stringify(game));
            expect(game.minFilter).toBe("LINEAR_MIPMAP_LINEAR");
            expect(game.magFilter).toBe("LINEAR");
            expect(game.levels).toBe(levels);
        });
    });

    it("draws the shadow darken after the opaque meshes and before the transparent ones under Game", async () => {
        await closeAll();
        await openInPreview(MODEL);
        await inNewestWebview(async () => {
            await loaded();
            await openSceneSettings();
            await pick("Game");

            const order = await drawOrder();
            console.log("PRV-56 draw order: " + JSON.stringify(order));
            const darken = order.findIndex((d) => d.darken);
            expect(darken).toBeGreaterThanOrEqual(0);

            const opaque = order.filter((d) => !d.darken && !d.transparent);
            const transparent = order.filter((d) => !d.darken && d.transparent);
            expect(opaque.length).toBeGreaterThan(0);
            expect(transparent.length).toBeGreaterThan(0);

            const late = opaque
                .filter((d) => order.indexOf(d) > darken)
                .map((d) => `${d.name} (${d.shader})`);
            const early = transparent
                .filter((d) => order.indexOf(d) < darken)
                .map((d) => `${d.name} (${d.shader})`);
            expect(late).toEqual([]);
            expect(early).toEqual([]);
        });
    });
});
