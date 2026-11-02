// PRV-08: frozen emitters - an asteroid field is full on its first frame and never moves again, wind and camera
// notwithstanding; the galaxy's dust holds still as well. Read from the simulation through the debug handle.
import { expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { emitters, model, openInPreview, openSceneSettings, setSlider } from "./preview.js";
import { cameraTo, type EmitterProbe, emitterProbes, emittersWhen, pause, probe } from "./particles.js";
import { inNewestWebview } from "./storyGraph.js";

const live = (all: EmitterProbe[]): number => all.reduce((s, e) => s + e.live, 0);
const state = (all: EmitterProbe[]): string =>
    JSON.stringify(all.map((e) => [e.live, e.ageSum.toFixed(3), e.positionSum.map((v) => v.toFixed(3))]));

async function frozenAndStill(file: string, names?: string[]): Promise<void> {
    await openInPreview(model(file));
    await inNewestWebview(async () => {
        const first = live(await emittersWhen((e) => live(e) > 0, 120_000, "Nothing was emitted"));
        await openSceneSettings();
        await setSlider("Wind speed", 10);
        const frozen = await emittersWhen((e) => e.length > 0 && e.every((x) => x.frozen), 30_000, "The emitters never froze");
        // Full on the first frame we can see: no shower filling it up.
        expect(first).toBeGreaterThanOrEqual(live(frozen) * 0.95);

        const settled = state(await emitterProbes());
        await pause(5_000);
        expect(state(await emitterProbes())).toBe(settled);
        const at = (await probe()).camera;
        await cameraTo([at.x * -1 + 1, at.y + 200, at.z * -1 + 1]);
        await pause(2_000);
        expect(state(await emitterProbes())).toBe(settled);
        if (names !== undefined) {
            expect(await emitters()).toEqual(names);
        }
    });
}

describe("PRV-08", () => {
    before(async () => {
        await serverReady();
    });

    it("populates the asteroid field at once and never moves it, wind or camera", async () => {
        await frozenAndStill("P_asteroid_field00.alo", ["inner", "outer"]);
    });

    it("holds the galaxy's dust, tendrils and filler still from the first frame", async () => {
        await frozenAndStill("P_galaxy.alo");
    });
});
