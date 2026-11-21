// PRV-60 (#431): a particle file opens with the whole effect in view, the camera outside it. A particle file
// draws no meshes, so the framing sphere had nothing to measure and fell back to one unit at the origin - the
// emitter's own position. P_fire_med00 opened with the camera inside the fire, the additive sprites filling
// the stage in flat yellow. The camera presets framed the same one-unit sphere, and so did the draw distance.
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { model, openInPreview, press } from "./preview.js";
import { emitterProbes, emittersWhen } from "./particles.js";
import { closeAll, inNewestWebview } from "./storyGraph.js";

interface Shot {
    /** From the camera to the point it looks at. */
    distance: number;
    /** Half of the narrower field of view, in radians. */
    half: number;
    near: number;
    far: number;
    /** What the preview takes the effect's reach to be. */
    reach: number;
}

/** Where the camera stands and what it can see. Run inside the preview. */
async function shot(): Promise<Shot> {
    return browser.execute(() => {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const v = (window as any).aetDebugViewport;
        const c = v.camera;
        const vertical = (c.fov * Math.PI) / 180;
        const horizontal = 2 * Math.atan(Math.tan(vertical / 2) * c.aspect);
        return {
            distance: c.position.distanceTo(v.controls.target),
            half: Math.min(vertical, horizontal) / 2,
            near: c.near,
            far: c.far,
            reach: v.particles.effectReach(),
        };
    });
}

/** The furthest any particle got from the emitter over `seconds`, in scene units. */
async function spread(seconds: number): Promise<number> {
    let furthest = 0;
    const until = Date.now() + seconds * 1_000;
    while (Date.now() < until) {
        for (const emitter of await emitterProbes()) {
            for (const particle of emitter.particles) {
                furthest = Math.max(furthest, Math.hypot(...particle.drawn));
            }
        }
        await browser.pause(200);
    }
    return furthest;
}

/** Picks an option of a radiogroup by its label. Run inside the preview. */
async function pick(group: string, option: string): Promise<void> {
    const found = await browser.execute(
        (wanted: { group: string; option: string }) => {
            const groups = Array.from(
                document.querySelectorAll('[role="radiogroup"]'),
            ).filter((g) => g.getAttribute("aria-label") === wanted.group);
            const radio = groups
                .flatMap((g) =>
                    Array.from(
                        g.querySelectorAll<HTMLButtonElement>('[role="radio"]'),
                    ),
                )
                .find((b) => (b.textContent ?? "").trim() === wanted.option);
            radio?.click();
            return radio !== undefined;
        },
        { group, option },
    );
    expect(found).toBe(true);
    await browser.pause(300);
}

describe("PRV-60", () => {
    before(async () => {
        await serverReady();
        await closeAll();
    });

    it("opens P_fire_med00 with the whole effect in view, and the presets and the draw distance keep it there", async () => {
        await openInPreview(model("P_fire_med00.alo"));
        await inNewestWebview(async () => {
            await emittersWhen(
                (e) => e.some((x) => x.live > 0),
                120_000,
                "The fire never drew",
            );

            const opening = await shot();
            const reached = await spread(5);
            console.log(
                "PRV-60 opening: " +
                    JSON.stringify({ ...opening, spread: reached }),
            );

            // Outside the effect, and far enough off that every particle is inside the view.
            expect(reached).toBeGreaterThan(0);
            expect(opening.distance).toBeGreaterThan(reached);
            expect(opening.distance * Math.sin(opening.half)).toBeGreaterThan(
                reached,
            );
            // Not a speck in an empty stage either. The sprites are far larger than the paths their
            // centres take, so the bound is loose: it is the centres that are measured here.
            expect(opening.distance * Math.sin(opening.half)).toBeLessThan(
                reached * 15,
            );
            expect(opening.near).toBeLessThan(opening.distance - reached);
            expect(opening.far).toBeGreaterThan(opening.distance + reached);

            // The presets stand as far off as the opening view.
            for (const preset of ["Top", "Front", "Side", "3/4"]) {
                await pick("Where the camera looks from", preset);
                const at = await shot();
                console.log(`PRV-60 ${preset}: ` + JSON.stringify(at));
                expect(at.distance).toBeCloseTo(opening.distance, 3);
            }

            // The draw distance is fitted to the same sphere: twice it, and back.
            await press('button[title="Camera settings"]');
            await pick("Draw distance", "2x");
            const doubled = await shot();
            expect(doubled.far).toBeCloseTo(opening.far * 2, 3);
            await pick("Draw distance", "Fit");
            const fitted = await shot();
            console.log("PRV-60 Fit: " + JSON.stringify(fitted));
            expect(fitted.far).toBeCloseTo(opening.far, 3);
            expect(fitted.near).toBeCloseTo(opening.near, 6);
            expect(fitted.distance).toBeCloseTo(opening.distance, 3);
        });
    });
});
