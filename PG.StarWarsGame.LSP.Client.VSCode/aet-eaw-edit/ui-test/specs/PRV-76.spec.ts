// PRV-76: a wreck a destroyed hardpoint sheds drifts the way the game moves it. The game adds a prop's
// `Debris_Movement_Vector` to the wreck's position in world axes every logic frame, whatever way the ship
// heads, and a ship at facing 0 heads along world +X. The previewed ship stands at facing 0, so the Star
// Destroyer's front left wreck, told -0.2 on X, drifts astern and down, not across the hull towards the
// centreline, and its front right wreck, told +0.2, drifts towards the bow.
import { browser, expect } from "@wdio/globals";
import { command, serverReady } from "../protocol.js";
import { press } from "./preview.js";
import { pause } from "./particles.js";
import { inNewestWebview } from "./storyGraph.js";

/** One wreck in the air: where its hardpoint was, where it is, and how long it has drifted. */
interface Wreck {
    key: string;
    move: { x: number; y: number; z: number };
    age: number;
    origin: number[];
    at: number[];
}

/** Every wreck the unit has shed. Run inside the preview. */
async function wrecks(): Promise<Wreck[]> {
    return browser.execute(() => {
        const rig = (
            window as unknown as {
                aetDebugViewport: {
                    destruction: {
                        breakoffs: {
                            key: string;
                            age: number;
                            prop: {
                                movementVector: {
                                    x: number;
                                    y: number;
                                    z: number;
                                };
                            };
                            origin: { x: number; y: number; z: number };
                            root: {
                                position: { x: number; y: number; z: number };
                            };
                        }[];
                    };
                };
            }
        ).aetDebugViewport.destruction;
        return rig.breakoffs.map((wreck) => ({
            key: wreck.key,
            move: wreck.prop.movementVector,
            age: wreck.age,
            origin: [wreck.origin.x, wreck.origin.y, wreck.origin.z],
            at: [
                wreck.root.position.x,
                wreck.root.position.y,
                wreck.root.position.z,
            ],
        }));
    });
}

/** How far a wreck has come, along the scene's axes: X is the ship's left, Y is up, Z is its bow. */
function drift(wreck: Wreck): { left: number; up: number; ahead: number } {
    return {
        left: wreck.at[0] - wreck.origin[0],
        up: wreck.at[1] - wreck.origin[1],
        ahead: wreck.at[2] - wreck.origin[2],
    };
}

describe("PRV-76", () => {
    before(async () => {
        await serverReady();
    });

    it("drifts a wreck along world axes from a ship at facing 0", async () => {
        await command(
            "aet-eaw-edit.lsp.previewAssembledUnit",
            "Star_Destroyer",
        );
        await inNewestWebview(async () => {
            let quiet = 0;
            await browser.waitUntil(
                async () => {
                    const done: boolean = await browser.execute(() => {
                        const v = (
                            window as unknown as {
                                aetDebugViewport?: {
                                    parts: Map<string, unknown>;
                                };
                            }
                        ).aetDebugViewport;
                        return (
                            v !== undefined &&
                            v.parts.size > 0 &&
                            document.querySelector(".load-cover") === null
                        );
                    });
                    quiet = done ? quiet + 1 : 0;
                    return quiet >= 3;
                },
                {
                    timeout: 180_000,
                    interval: 1000,
                    timeoutMsg: "The Star Destroyer did not finish loading",
                },
            );
            await pause(1_500);

            await press('button.rotary-pos[title^="Gameplay"]');
            await pause(1_500);
            await press('button[title="Destroy all hardpoints"]');
            await browser.waitUntil(async () => (await wrecks()).length >= 5, {
                timeout: 60_000,
                timeoutMsg: "The Star Destroyer shed no wrecks",
            });
            await pause(3_000);

            const shed = await wrecks();
            console.log("PRV-76 wrecks: " + JSON.stringify(shed));

            const of = (key: string): Wreck => {
                const wreck = shed.find((candidate) => candidate.key === key);
                if (wreck === undefined) {
                    throw new Error(`no wreck of ${key}`);
                }
                return wreck;
            };

            // Thirty logic frames a second; a wreck two seconds old has drifted sixty times its vector.
            const frames = (wreck: Wreck): number => wreck.age / 0.03333;

            // Front left: -0.2 on world X is away from the heading, -0.5 on world Z is down. Nothing sideways.
            const frontLeft = of("HP_Star_Destroyer_Weapon_FL");
            const left = drift(frontLeft);
            expect(frontLeft.move).toEqual({ x: -0.2, y: 0, z: -0.5 });
            expect(Math.abs(left.left)).toBeLessThan(0.5);
            expect(
                Math.abs(left.ahead - -0.2 * frames(frontLeft)),
            ).toBeLessThan(0.5);
            expect(Math.abs(left.up - -0.5 * frames(frontLeft))).toBeLessThan(
                0.5,
            );

            // Front right: +0.2 on world X is along the heading.
            const frontRight = of("HP_Star_Destroyer_Weapon_FR");
            const right = drift(frontRight);
            expect(frontRight.move).toEqual({ x: 0.2, y: 0, z: -0.5 });
            expect(Math.abs(right.left)).toBeLessThan(0.5);
            expect(
                Math.abs(right.ahead - 0.2 * frames(frontRight)),
            ).toBeLessThan(0.5);

            // The shield generator: +0.5 along the heading and +0.5 up.
            const generator = of("HP_Star_Destroyer_Shield_Generator");
            const up = drift(generator);
            expect(Math.abs(up.left)).toBeLessThan(0.5);
            expect(Math.abs(up.ahead - 0.5 * frames(generator))).toBeLessThan(
                0.5,
            );
            expect(Math.abs(up.up - 0.5 * frames(generator))).toBeLessThan(0.5);
        });
    });
});
