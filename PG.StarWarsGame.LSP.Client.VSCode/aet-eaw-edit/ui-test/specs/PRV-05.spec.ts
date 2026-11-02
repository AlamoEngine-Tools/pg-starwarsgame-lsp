// PRV-05: wind on an ordinary emitter declared affected by it accelerates its particles over their life - the
// speed along the wind grows with age, half the wind per second - rather than a push at birth; an emitter not
// declared affected does not lean. Read from the simulation through the viewport's debug handle.
import { expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { model, openInPreview, openSceneSettings, setSlider } from "./preview.js";
import { correlation, type EmitterProbe, emitterProbes, emittersWhen, pause, slope } from "./particles.js";
import { inNewestWebview } from "./storyGraph.js";

/** Per emitter: the slope of the speed along +x over age, collected over a few frames. */
async function slopes(): Promise<{ name: string; slope: number; r: number }[]> {
    const ages = new Map<string, number[]>();
    const speeds = new Map<string, number[]>();
    for (let i = 0; i < 6; i++) {
        (await emitterProbes()).forEach((e: EmitterProbe, index) => {
            const key = `${index}:${e.name}`;
            ages.set(key, [...(ages.get(key) ?? []), ...e.particles.map((p) => p.age)]);
            speeds.set(key, [...(speeds.get(key) ?? []), ...e.particles.map((p) => p.velocity[0])]);
        });
        await pause(500);
    }
    return [...ages.keys()]
        .filter((k) => ages.get(k)!.length >= 20)
        .map((k) => ({ name: k, slope: slope(ages.get(k)!, speeds.get(k)!), r: correlation(ages.get(k)!, speeds.get(k)!) }));
}

async function underWind(file: string, speed: number): Promise<{ name: string; slope: number; r: number }[]> {
    await openInPreview(model(file));
    return inNewestWebview(async () => {
        await emittersWhen((e) => e.some((x) => x.live > 10), 120_000, "Nothing was emitted");
        await openSceneSettings();
        await setSlider("Wind direction", 0);
        await setSlider("Wind speed", speed);
        await pause(6_000);
        return slopes();
    });
}

describe("PRV-05", () => {
    before(async () => {
        await serverReady();
    });

    it("bends the smoke of an emitter affected by wind into a curve", async () => {
        const calm = await underWind("P_commjamm_damage_00.alo", 0);
        const blown = await underWind("P_commjamm_damage_00.alo", 10);
        // The emitter the wind bends is the one whose speed tracks its age; an emitter not affected scatters
        // around zero and says nothing. Half the wind of ten per second of age: an acceleration that accumulates.
        const bent = blown.reduce((best, b) => (b.r > best.r ? b : best));
        expect(bent.r).toBeGreaterThan(0.8);
        const gained = bent.slope - (calm.find((c) => c.name === bent.name)?.slope ?? 0);
        expect(gained).toBeGreaterThan(4);
        expect(gained).toBeLessThan(6);
    });

    it("leaves the smoke of an emitter not affected by wind upright", async () => {
        const calm = await underWind("P_smoke_med00.alo", 0);
        const blown = await underWind("P_smoke_med00.alo", 10);
        for (const b of blown) {
            expect(Math.abs(b.slope - (calm.find((c) => c.name === b.name)?.slope ?? 0))).toBeLessThan(1);
        }
    });
});
