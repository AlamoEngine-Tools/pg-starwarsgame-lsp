// PRV-42: "Bend the frame" must not move the picture where nothing bends it. The heat pass clears its
// distortion field to a neutral value and reads the scene back through it; 0.5.1 handed that neutral to the
// canvas as a linear colour, so the canvas stored 188 instead of 128 and every pixel was read from 2.35% of the
// canvas up and to the right. The model, its handles and its arcs slid away from the bone tags, reticles and
// the pointer's hit test, all by that one vector.
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { foc, serverReady } from "../protocol.js";
import { openInPreview } from "./preview.js";
import { closeAll, inNewestWebview } from "./storyGraph.js";

interface Bent {
    field: number[];
    differing: number;
    size: number[];
}

/**
 * The neutral field, and how much of the frame the heat pass changes with no heat sprite in it.
 *
 * The debug view builds the pass and draws the field itself; the plain render and the pass's own
 * render then draw the same scene with the same camera, so with nothing bending the two must match.
 */
async function bend(): Promise<Bent> {
    await browser.waitUntil(
        async () =>
            browser.execute(() => {
                const v = (window as unknown as { aetDebugViewport?: { parts: Map<string, unknown> } })
                    .aetDebugViewport;
                return v !== undefined && v.parts.size > 0;
            }),
        { timeout: 120_000, timeoutMsg: "The model did not load" },
    );
    await browser.pause(2_000);
    return browser.execute(() => {
        const v = (window as unknown as { aetDebugViewport: any }).aetDebugViewport;
        const gl = v.renderer.getContext() as WebGL2RenderingContext;
        const width = gl.drawingBufferWidth;
        const height = gl.drawingBufferHeight;

        v.setHeat(true);
        v.setHeatDebug(true);
        v.renderFrame();
        const field = new Uint8Array(4);
        gl.readPixels(4, 4, 1, 1, gl.RGBA, gl.UNSIGNED_BYTE, field);
        v.setHeatDebug(false);

        v.renderer.setRenderTarget(null);
        v.renderer.render(v.scene, v.camera);
        const plain = new Uint8Array(width * height * 4);
        gl.readPixels(0, 0, width, height, gl.RGBA, gl.UNSIGNED_BYTE, plain);

        v.heat.render(v.renderer, v.scene, v.camera, false);
        const bent = new Uint8Array(width * height * 4);
        gl.readPixels(0, 0, width, height, gl.RGBA, gl.UNSIGNED_BYTE, bent);
        v.renderFrame();

        let differing = 0;
        for (let i = 0; i < plain.length; i += 4) {
            if (
                Math.abs(plain[i] - bent[i]) > 2 ||
                Math.abs(plain[i + 1] - bent[i + 1]) > 2 ||
                Math.abs(plain[i + 2] - bent[i + 2]) > 2
            ) {
                differing++;
            }
        }

        return {
            field: Array.from(field),
            differing: differing / (width * height),
            size: [width, height],
        };
    });
}

describe("PRV-42", () => {
    before(async () => {
        await serverReady();
    });

    it("leaves the frame where it is when nothing bends it", async () => {
        await openInPreview(path.join(foc, "Data", "Art", "Models", "Hp_turret_front_00.alo"));
        const bent = await inNewestWebview(bend);
        console.log(`PRV-42 ${JSON.stringify(bent)}`);

        // The field the composite subtracts 128/255 from: anything else is a pull across the whole frame.
        for (const channel of bent.field.slice(0, 3)) {
            expect(Math.abs(channel - 128)).toBeLessThanOrEqual(1);
        }
        // And the picture the pass draws is the picture the plain render draws.
        expect(bent.differing).toBeLessThan(0.001);
    });

    after(async () => {
        await closeAll();
    });
});
