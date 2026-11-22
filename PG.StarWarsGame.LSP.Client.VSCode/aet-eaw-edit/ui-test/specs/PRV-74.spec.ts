// PRV-74: a ship's engine glow follows its speed, and the states done to it can be put on by hand. In the
// game the space locomotor sets the engine effects to a brightness of 0.2 at rest rising to 1 at full speed,
// flickers them at up to 0.2 while the engines are disabled for a time, and a unit with the ion stun behaviour
// shows its `pi` effects only while it is ion stunned. The Star Destroyer's speed slider, its Ion stun toggle
// and its Engines disabled toggle do each of those.
import { browser, expect } from "@wdio/globals";
import { command, serverReady } from "../protocol.js";
import { pause } from "./particles.js";
import { inNewestWebview } from "./storyGraph.js";

/** The unit's own effects of one group: how many are drawn, and the brightness each emitter is drawn at. */
interface Group {
    drawn: number;
    hidden: number;
    brightness: number[];
}

/** Reads the effects whose name starts with `prefix`. Run inside the preview. */
async function group(prefix: string): Promise<Group> {
    return browser.execute((start: string) => {
        const rig = (
            window as unknown as {
                aetDebugViewport: {
                    particles: {
                        particleSystems: Map<
                            string,
                            {
                                origin: string;
                                system: { name: string };
                                instance: { root: { visible: boolean }; emitters: { brightness: number }[] };
                            }
                        >;
                    };
                };
            }
        ).aetDebugViewport.particles;
        const out: Group = { drawn: 0, hidden: 0, brightness: [] };
        for (const entry of rig.particleSystems.values()) {
            if (entry.origin !== "model" || !entry.system.name.toLowerCase().startsWith(start)) {
                continue;
            }
            if (entry.instance.root.visible) {
                out.drawn++;
            } else {
                out.hidden++;
            }
            out.brightness.push(...entry.instance.emitters.map((emitter) => emitter.brightness));
        }
        return out;
    }, prefix);
}

/** Moves the speed slider to a percentage, the way a reader does. Run inside the preview. */
async function speed(percent: number): Promise<void> {
    await browser.execute((value: number) => {
        const slider = document.querySelector<HTMLInputElement>('.stage-field.speed input[type="range"]');
        if (slider === null) {
            throw new Error("no speed slider");
        }
        const set = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!;
        set.call(slider, String(value));
        slider.dispatchEvent(new Event("input", { bubbles: true }));
    }, percent);
    await pause(500);
}

/** Presses a button of the stage by its title. Run inside the preview. */
async function toggle(title: string): Promise<void> {
    const button = await browser.$(`button[title="${title}"]`);
    await button.waitForExist({ timeout: 30_000 });
    await browser.execute((b) => (b as unknown as HTMLElement).click(), button);
    await pause(700);
}

describe("PRV-74", () => {
    before(async () => {
        await serverReady();
    });

    it("draws the engine glow as bright as the ship is fast, flickers it, and shows the ion effects on a stun", async () => {
        await command("aet-eaw-edit.lsp.previewAssembledUnit", "Star_Destroyer");
        await inNewestWebview(async () => {
            let quiet = 0;
            await browser.waitUntil(
                async () => {
                    const done: boolean = await browser.execute(() => {
                        const v = (window as unknown as { aetDebugViewport?: { parts: Map<string, unknown> } })
                            .aetDebugViewport;
                        return v !== undefined && v.parts.size > 0 && document.querySelector(".load-cover") === null;
                    });
                    quiet = done ? quiet + 1 : 0;
                    return quiet >= 3;
                },
                { timeout: 180_000, interval: 1000, timeoutMsg: "The Star Destroyer did not finish loading" },
            );
            await pause(1_500);

            // Opens at full speed: the glow as the file has it.
            const full = await group("pe");
            await speed(0);
            const atRest = await group("pe");
            await speed(50);
            const half = await group("pe");
            await speed(100);
            console.log("PRV-74 speed: " + JSON.stringify({ full, atRest, half }));

            expect(full.brightness.length).toBeGreaterThan(0);
            expect(full.brightness.every((b) => b === 1)).toBe(true);
            expect(atRest.brightness.every((b) => Math.abs(b - 0.2) < 1e-6)).toBe(true);
            expect(half.brightness.every((b) => Math.abs(b - 0.6) < 1e-6)).toBe(true);

            // The file hides the Star Destroyer's ion sparks; a stun shows them, and its end hides them.
            const calm = await group("pi");
            await toggle("Ion stun");
            const stunned = await group("pi");
            await toggle("Ion stun");
            const over = await group("pi");
            console.log("PRV-74 stun: " + JSON.stringify({ calm, stunned, over }));

            expect(calm.drawn + calm.hidden).toBeGreaterThan(0);
            expect(calm.drawn).toBe(0);
            expect(stunned.hidden).toBe(0);
            expect(over.drawn).toBe(0);

            // Disabled for a time: still drawn, at a fifth of its brightness at most, and not steady.
            await toggle("Engines disabled");
            const flicker: number[] = [];
            let disabled: Group = await group("pe");
            for (let i = 0; i < 8; i++) {
                disabled = await group("pe");
                flicker.push(disabled.brightness[0]);
                await pause(120);
            }
            await toggle("Engines disabled");
            const back = await group("pe");
            console.log("PRV-74 disabled: " + JSON.stringify({ flicker, drawn: disabled.drawn, back }));

            expect(disabled.hidden).toBe(0);
            expect(flicker.every((b) => b >= 0 && b <= 0.2 + 1e-6)).toBe(true);
            expect(new Set(flicker).size).toBeGreaterThan(1);
            expect(back.brightness.every((b) => b === 1)).toBe(true);
        });
    });
});
