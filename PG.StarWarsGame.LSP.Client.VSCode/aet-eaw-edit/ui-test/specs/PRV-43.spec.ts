// PRV-43 (#440): an animation card plays its clip again. A card read as playing for as long as its clip was the
// current one, so a clip that plays once went on showing a pause glyph after it had ended, and pressing the card
// set the name already set - nothing re-ran, and only the scrubber could play the clip again. Measured on the
// Stormtrooper's Chokedeath (4 s, plays once). The card pauses and resumes its clip as well, and a take pressed
// after it ended plays again too.
import { browser, expect } from "@wdio/globals";
import { command, serverReady } from "../protocol.js";
import { closeAll, inNewestWebview } from "./storyGraph.js";

interface Clip {
    name: string;
    time: number;
    duration: number;
    running: boolean;
}

interface Card {
    pressed: string | null;
    glyph: string;
}

/** The clip the viewport is playing, read off the debug handle. */
async function clip(): Promise<Clip | null> {
    return browser.execute(() => {
        const v = (
            window as unknown as {
                aetDebugViewport?: {
                    animation: {
                        active: {
                            action: {
                                time: number;
                                paused: boolean;
                                isRunning(): boolean;
                                getClip(): { name: string; duration: number };
                            } | null;
                        };
                    };
                };
            }
        ).aetDebugViewport;
        const action = v?.animation.active.action ?? null;
        return action === null
            ? null
            : {
                  name: action.getClip().name,
                  time: action.time,
                  duration: action.getClip().duration,
                  running: action.isRunning() && !action.paused,
              };
    });
}

/** The play button of the card labelled `label`: whether it reads as pressed, and which glyph it draws. */
async function card(label: string): Promise<Card> {
    return browser.execute((name: string) => {
        const button = document.querySelector(`.anim-card .anim-play[aria-label="${name}"]`);
        return {
            pressed: button?.getAttribute("aria-pressed") ?? null,
            glyph: button?.querySelector("svg")?.getAttribute("class") ?? "",
        };
    }, label);
}

async function press(selector: string): Promise<void> {
    const element = await browser.$(selector);
    await element.waitForExist({ timeout: 30_000 });
    await browser.execute((e) => (e as unknown as HTMLElement).click(), element);
    await browser.pause(600);
}

async function untilEnded(): Promise<void> {
    await browser.waitUntil(
        async () => {
            const now = await clip();
            return now !== null && !now.running && now.time >= now.duration - 1e-3;
        },
        { timeout: 30_000, timeoutMsg: "The clip did not run to its end" },
    );
    // The panel reads the playhead every 50 ms.
    await browser.pause(400);
}

describe("PRV-43", () => {
    before(async () => {
        await serverReady();
    });

    it("plays a clip again from its card and its take, and pauses it from the card", async () => {
        await command("aet-eaw-edit.lsp.previewAssembledUnit", "Stormtrooper");
        const seen = await inNewestWebview(async () => {
            await press('button.rotary-pos[title^="Animation"]');
            const chokedeath = '.anim-card .anim-play[aria-label="Chokedeath"]';

            // A card ran to its end, and pressed again.
            await press(chokedeath);
            await untilEnded();
            const ended = await card("Chokedeath");
            await press(chokedeath);
            const again = await clip();

            // The card pauses its running clip, and resumes it.
            const before = await clip();
            await press(chokedeath);
            const paused = await clip();
            const pausedCard = await card("Chokedeath");
            await press(chokedeath);
            await browser.pause(400);
            const resumed = await clip();

            // A take ran to its end, and pressed again.
            const take = '.anim-card .anim-take[title="ei_trooper_die_00"]';
            await press(take);
            await untilEnded();
            await press(take);
            const takeAgain = await clip();

            return { ended, again, before, paused, pausedCard, resumed, takeAgain };
        });
        console.log(`PRV-43 ${JSON.stringify(seen)}`);

        // Ended: the card no longer reads as playing, and pressing it plays the clip from the start.
        expect(seen.ended.pressed).toBe("false");
        expect(seen.ended.glyph).toContain("player-play");
        expect(seen.again?.name).toBe("ei_trooper_chokedeath_00");
        expect(seen.again?.running).toBe(true);
        expect(seen.again!.time).toBeLessThan(1);

        // Running: the card pauses it where it is, and a second press carries on from there.
        expect(seen.paused?.running).toBe(false);
        expect(seen.paused!.time).toBeGreaterThanOrEqual(seen.before!.time);
        expect(seen.pausedCard.pressed).toBe("false");
        expect(seen.resumed?.running).toBe(true);
        expect(seen.resumed!.time).toBeGreaterThan(seen.paused!.time);

        // A take pressed after it ended plays again from the start.
        expect(seen.takeAgain?.name).toBe("ei_trooper_die_00");
        expect(seen.takeAgain?.running).toBe(true);
        expect(seen.takeAgain!.time).toBeLessThan(1);
    });

    after(async () => {
        await closeAll();
    });
});
