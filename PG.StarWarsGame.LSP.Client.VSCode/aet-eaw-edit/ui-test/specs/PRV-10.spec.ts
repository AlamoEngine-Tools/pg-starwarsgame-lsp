// PRV-10: the heat haze is hidden by the hull in front of it. Read off the heat Debug view, which draws the
// heat BUFFER on the canvas: from fourteen sides the hull never adds heat, and from at least one it hides some -
// compared against the same view with the hull hidden, so no side has to be known in advance.
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { model, openInPreview } from "./preview.js";
import { emittersWhen, pause } from "./particles.js";
import { inNewestWebview } from "./storyGraph.js";

type Handle = {
    setHeat(on: boolean): void;
    setHeatDebug(on: boolean): void;
    setPartHidden(id: string, hidden: boolean): void;
    applyCameraPose(p: object, t: object): void;
    canvasCoverage(): { lit: number; total: number };
    subjectSphere: { center: { x: number; y: number; z: number }; radius: number };
    treeItems(): { id: string; name: string }[];
    applyRowOverrides(changes: { row: string; override: "shown" | "hidden" | null }[]): void;
};

async function coverageAround(): Promise<{ shown: number; hidden: number }[]> {
    return browser.execute(() => {
        const v = (window as unknown as { aetDebugViewport: Handle }).aetDebugViewport;
        const { center, radius } = v.subjectSphere;
        const out: { shown: number; hidden: number }[] = [];
        const d = radius * 1.6;
        const poses: [number, number, number][] = [];
        for (let i = 0; i < 12; i++) {
            const a = (i / 12) * Math.PI * 2;
            poses.push([Math.cos(a) * d, d * 0.25, Math.sin(a) * d]);
        }
        poses.push([0, d, 0.01], [0, -d, 0.01]);
        for (const [x, y, z] of poses) {
            v.applyCameraPose({ x: center.x + x, y: center.y + y, z: center.z + z }, center);
            v.setPartHidden("hull", false);
            const shown = v.canvasCoverage().lit;
            v.setPartHidden("hull", true);
            const hidden = v.canvasCoverage().lit;
            v.setPartHidden("hull", false);
            out.push({ shown, hidden });
        }
        return out;
    });
}

describe("PRV-10", () => {
    before(async () => {
        await serverReady();
    });

    it("hides the haze behind the hull in the heat buffer", async () => {
        await openInPreview(model("Nv_sandspeeder.alo"));
        await inNewestWebview(async () => {
            // The haze's proxy does not run as the model opens (measured: its system emits nothing); its
            // eye in the model tree switches it on, as a reader would.
            await browser.waitUntil(
                async () =>
                    browser.execute(() => {
                        const v = (window as unknown as { aetDebugViewport: Handle }).aetDebugViewport;
                        const row = v.treeItems().find((i) => /p_sandspeederheat/i.test(i.name));
                        if (row !== undefined) v.applyRowOverrides([{ row: row.id, override: "shown" }]);
                        return row !== undefined;
                    }),
                { timeout: 120_000, timeoutMsg: "No p_sandspeederheat row in the model tree" },
            );
            await emittersWhen((e) => e.some((x) => x.live > 0), 120_000, "The haze never ran");
            await browser.execute(() => {
                const v = (window as unknown as { aetDebugViewport: Handle }).aetDebugViewport;
                v.setHeat(true);
                v.setHeatDebug(true);
            });
            await pause(2_000);
            const sides = await coverageAround();
            console.log("PRV-10 heat coverage: " + JSON.stringify(sides));
            expect(sides.some((s) => s.hidden > 0)).toBe(true);
            // The hull only ever takes heat away, never adds it.
            for (const s of sides) expect(s.shown).toBeLessThanOrEqual(s.hidden * 1.05 + 50);
            // And from at least one side it hides a good part of it.
            expect(sides.some((s) => s.hidden > 200 && s.shown < s.hidden * 0.8)).toBe(true);
            await browser.execute(() => (window as unknown as { aetDebugViewport: Handle }).aetDebugViewport.setHeatDebug(false));
        });
    });
});
