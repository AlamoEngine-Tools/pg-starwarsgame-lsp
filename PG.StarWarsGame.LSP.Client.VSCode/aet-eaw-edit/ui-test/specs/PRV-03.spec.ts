// PRV-03: a weather field declared affected by wind drifts with five times the viewer's wind, the other way at
// heading 180, not at all at speed 0; one not declared affected stays put; tailed rain leans along the drift.
// Heading 0 is +x in the viewport's wind (setWind), so the drift is asserted on x.
import { expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { model, openInPreview, openSceneSettings, setSlider } from "./preview.js";
import { emitterProbes, emittersWhen, mean, pause } from "./particles.js";
import { inNewestWebview } from "./storyGraph.js";

async function wind(direction: number, speed: number): Promise<void> {
    await setSlider("Wind direction", direction);
    await setSlider("Wind speed", speed);
    await pause(1_500);
}

const fieldDrift = async (): Promise<number[]> => (await emitterProbes()).filter((e) => e.weather).map((e) => e.drift[0]);

describe("PRV-03", () => {
    before(async () => {
        await serverReady();
    });

    it("drifts a field affected by wind with five times the wind, and reverses it at heading 180", async () => {
        await openInPreview(model("P_snowsleet.alo"));
        await inNewestWebview(async () => {
            await emittersWhen((e) => e.some((x) => x.weather && x.live > 0), 120_000, "No weather field ran");
            await openSceneSettings();
            await wind(0, 10);
            const east = await fieldDrift();
            expect(Math.max(...east.map(Math.abs))).toBeCloseTo(50, 0);
            await wind(180, 10);
            const west = await fieldDrift();
            expect(Math.min(...west)).toBeCloseTo(-50, 0);
            await wind(180, 0);
            expect((await fieldDrift()).every((d) => Math.abs(d) < 1e-6)).toBe(true);
        });
    });

    it("leaves a field not declared affected by wind where it is", async () => {
        await openInPreview(model("P_snowblizzard.alo"));
        await inNewestWebview(async () => {
            await emittersWhen((e) => e.some((x) => x.weather && x.live > 0), 120_000, "No weather field ran");
            await openSceneSettings();
            await wind(0, 10);
            expect((await fieldDrift()).every((d) => Math.abs(d) < 1e-6)).toBe(true);
        });
    });

    it("leans tailed rain along the drift", async () => {
        await openInPreview(model("P_rain_med00.alo"));
        await inNewestWebview(async () => {
            await emittersWhen((e) => e.some((x) => x.weather && x.live > 0), 120_000, "No weather field ran");
            await openSceneSettings();
            await wind(0, 0);
            const still = mean((await emitterProbes()).filter((e) => e.weather).flatMap((e) => e.particles.map((p) => p.velocity[0])));
            await wind(0, 10);
            const blown = mean((await emitterProbes()).filter((e) => e.weather).flatMap((e) => e.particles.map((p) => p.velocity[0])));
            // The streak follows the velocity the field is drawn with, the drift on top of the fall.
            expect(blown - still).toBeGreaterThan(40);
        });
    });
});
