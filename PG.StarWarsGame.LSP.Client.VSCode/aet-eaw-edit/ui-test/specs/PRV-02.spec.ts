// PRV-02: a weather system is a field around the camera - full from the first frame, constant over time, kept
// around the camera wherever it goes; Weather intensity draws its share. Read from the simulation through the
// viewport's debug handle. The fade near the lens is weatherFade's, covered by the webview unit tests.
import { expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { emitters, fieldInfo, model, openInPreview, openSceneSettings, sectionFields, setSlider, sliderRange } from "./preview.js";
import { cameraTo, emitterProbes, emittersWhen, mean, pause, probe } from "./particles.js";
import { inNewestWebview } from "./storyGraph.js";

const weatherLive = (all: { weather: boolean; live: number }[]): number =>
    all.filter((e) => e.weather).reduce((s, e) => s + e.live, 0);
const weatherDrawn = (all: { weather: boolean; drawn: number }[]): number =>
    all.filter((e) => e.weather).reduce((s, e) => s + e.drawn, 0);

describe("PRV-02", () => {
    before(async () => {
        await serverReady();
    });

    it("fills the field at once, keeps it full around the camera, and draws the intensity's share", async () => {
        await openInPreview(model("P_snowblizzard.alo"));
        await inNewestWebview(async () => {
            const first = weatherLive(await emittersWhen((e) => weatherLive(e) > 0, 120_000, "No weather field ran"));
            await openSceneSettings();
            await setSlider("Wind speed", 0);
            await pause(3_000);
            const full = weatherLive(await emitterProbes());
            // Full from the first frame we can see: no shower filling it up.
            expect(first).toBeGreaterThanOrEqual(full * 0.95);

            // Nothing leaves for good: the count holds over ten seconds.
            for (let i = 0; i < 5; i++) {
                await pause(2_000);
                expect(Math.abs(weatherLive(await emitterProbes()) - full)).toBeLessThanOrEqual(full * 0.05);
            }

            // The field stays around the camera: moved far away, the drawn copies follow it.
            const centreOffset = async (): Promise<number[]> => {
                const p = await probe();
                const drawn = p.systems.flatMap((s) => s.emitters.filter((e) => e.weather).flatMap((e) => e.particles));
                const c = [p.camera.x, p.camera.y, p.camera.z];
                return [0, 1, 2].map((axis) => mean(drawn.map((d) => d.drawn[axis])) - c[axis]);
            };
            const before = await centreOffset();
            const camera = (await probe()).camera;
            await cameraTo([camera.x + 500, camera.y, camera.z + 500], [500, 0, 500]);
            await pause(1_000);
            const after = await centreOffset();
            expect(Math.hypot(after[0] - before[0], after[2] - before[2])).toBeLessThan(150);
            expect(weatherDrawn(await emitterProbes())).toBeGreaterThan(0);

            // Weather intensity draws its share.
            const intensity = (await sectionFields("Weather")).find((f) => f.label === "Weather intensity");
            expect(intensity?.disabled).toBe(false);
            expect(await fieldInfo("Weather intensity")).toBe("Share of a weather field drawn, as a weather phase sets it");
            const range = await sliderRange("Weather intensity");
            const [min, max] = [Number(range.min), Number(range.max)];
            await setSlider("Weather intensity", min);
            await pause(1_000);
            expect(weatherDrawn(await emitterProbes())).toBe(0);
            await setSlider("Weather intensity", (min + max) / 2);
            await pause(1_000);
            const half = await emitterProbes();
            expect(Math.abs(weatherDrawn(half) - weatherLive(half) / 2)).toBeLessThanOrEqual(weatherLive(half) * 0.05);
            await setSlider("Weather intensity", max);
            await pause(1_000);
            const all = await emitterProbes();
            expect(weatherDrawn(all)).toBe(weatherLive(all));

            expect(await emitters()).toEqual(["Sleet", "Sleet"]);
        });
    });
});
