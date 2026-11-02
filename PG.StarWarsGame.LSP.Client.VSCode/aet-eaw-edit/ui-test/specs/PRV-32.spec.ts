// PRV-32: a frozen alpha batch is drawn back to front, re-sorted as the camera turns; a frozen bump field keeps
// its bake order. Read from the simulation through the debug handle: the depths each frame was drawn in.
import { expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { model, openInPreview } from "./preview.js";
import { cameraTo, emitterProbes, emittersWhen, pause, probe } from "./particles.js";
import { inNewestWebview } from "./storyGraph.js";

const farToNear = (depths: number[]): boolean => depths.every((d, i) => i === 0 || d <= depths[i - 1] + 1e-3);

describe("PRV-32", () => {
    before(async () => {
        await serverReady();
    });

    it("draws the frozen chunks back to front from every side, re-sorting as the camera turns", async () => {
        await openInPreview(model("P_ds_chunks1.alo"));
        await inNewestWebview(async () => {
            await emittersWhen((e) => e.length > 0 && e.every((x) => x.frozen), 120_000, "The chunks never froze");
            const sorted = (await emitterProbes()).filter((e) => e.drawDepths !== null);
            expect(sorted.length).toBeGreaterThan(0);
            const camera = (await probe()).camera;
            const radius = Math.hypot(camera.x, camera.y, camera.z);
            for (let i = 0; i < 6; i++) {
                const a = (i / 6) * Math.PI * 2;
                await cameraTo([Math.cos(a) * radius, camera.y, Math.sin(a) * radius]);
                await pause(700);
                for (const e of (await emitterProbes()).filter((x) => x.drawDepths !== null)) {
                    expect(farToNear(e.drawDepths!)).toBe(true);
                }
            }
        });
    });

    it("keeps the asteroid field's bake order", async () => {
        await openInPreview(model("P_asteroid_field00.alo"));
        await inNewestWebview(async () => {
            await emittersWhen((e) => e.length > 0 && e.every((x) => x.frozen), 120_000, "The field never froze");
            expect((await emitterProbes()).every((e) => e.drawDepths === null)).toBe(true);
        });
    });
});
