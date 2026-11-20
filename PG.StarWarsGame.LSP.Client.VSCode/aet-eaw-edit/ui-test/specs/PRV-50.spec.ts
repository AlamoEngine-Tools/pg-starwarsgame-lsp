// PRV-50 (#447): a non-looping clip that has finished holds its second-to-last frame, pose and visibility.
// The X-wing's deploy, Rv_xwing_deploy_00, has 31 frames: its track for the `pe_Z95engines` bone under
// `engines01` hides the bone on frame 29 and shows it on frame 30, so in the game the deployed X-wing holds
// that engine effect off. Before, the preview held frame 30 and the effect ran, and the scrubber ended one
// frame late.
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { model, openInPreview } from "./preview.js";
import { closeAll, inNewestWebview } from "./storyGraph.js";

/** One model tree row, as the preview resolved it, with the name of the row it sits under. */
interface Row {
    name: string;
    parent: string;
    visible: boolean;
    because?: string;
}

/** What the player and the tree say once the clip has run off its end. */
interface End {
    time: number;
    duration: number;
    running: boolean;
    engines: Row[];
}

describe("PRV-50", () => {
    before(async () => {
        await serverReady();
        await closeAll();
    });

    it("holds the X-wing's deploy on its second-to-last frame once it has finished", async () => {
        await openInPreview(model("Rv_xwing.alo"));
        await inNewestWebview(async () => {
            await browser.waitUntil(
                async () =>
                    browser.execute(() => {
                        const v = (
                            window as unknown as {
                                aetDebugViewport?: {
                                    parts: Map<string, unknown>;
                                    animationNames: string[];
                                };
                            }
                        ).aetDebugViewport;
                        return (
                            v !== undefined &&
                            v.parts.size > 0 &&
                            v.animationNames.length > 0
                        );
                    }),
                {
                    timeout: 120_000,
                    timeoutMsg: "The X-wing or its clips did not load",
                },
            );
            await browser.pause(2_000);

            await browser.execute(() => {
                // eslint-disable-next-line @typescript-eslint/no-explicit-any
                const v = (window as unknown as { aetDebugViewport: any })
                    .aetDebugViewport;
                const name =
                    (v.animationNames as string[]).find(
                        (n) => n.toLowerCase() === "rv_xwing_deploy_00",
                    ) ?? "rv_xwing_deploy_00";
                v.setAnimationLoop(false);
                v.setPaused(false);
                v.play(name);
            });

            // A one-second clip, played by the render loop rather than seeked, so the mixer is what ends it.
            await browser.pause(4_000);

            const end: End = await browser.execute(() => {
                // eslint-disable-next-line @typescript-eslint/no-explicit-any
                const v = (window as unknown as { aetDebugViewport: any })
                    .aetDebugViewport;
                v.rows.resolveRows();
                const items = v.treeItems() as {
                    id: string;
                    parentId: string | null;
                    kind: string;
                    name: string;
                    visible: boolean;
                    because?: string;
                }[];
                const byId = new Map(items.map((r) => [r.id, r]));
                const progress = v.animationProgress() as {
                    time: number;
                    duration: number;
                    running: boolean;
                };
                return {
                    ...progress,
                    engines: items
                        .filter(
                            (r) =>
                                r.kind === "particle" &&
                                /^pe_z95engines/i.test(r.name),
                        )
                        .map((r) => ({
                            name: r.name,
                            parent:
                                (r.parentId === null
                                    ? undefined
                                    : byId.get(r.parentId)?.name) ?? "",
                            visible: r.visible,
                            because: r.because,
                        })),
                };
            });
            console.log("PRV-50 end: " + JSON.stringify(end));

            expect(end.running).toBe(false);
            expect(Math.abs(end.time - 29 / 30)).toBeLessThan(1e-3);
            expect(Math.abs(end.duration - 29 / 30)).toBeLessThan(1e-3);

            const held = end.engines.filter(
                (r) => r.parent.toLowerCase() === "engines01",
            );
            expect(held).toHaveLength(1);
            expect(held[0].visible).toBe(false);
            expect(held[0].because).toBe("animation");
        });
    });
});
