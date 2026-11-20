// PRV-48 (#446): the model file's bone visible flag decides the parts on that bone while no clip runs. The
// X-wing carries two `pe_z95engines` effects on two bones that share the name `engines`; the file marks the
// one under `engines01` invisible, so in its base pose the game runs one of them, not two. Before, the
// preview ran both.
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { model, openInPreview } from "./preview.js";
import { closeAll, inNewestWebview } from "./storyGraph.js";

/** One model tree row, as the preview resolved it. */
interface Row {
    kind: string;
    name: string;
    visible: boolean;
    because?: string;
}

describe("PRV-48", () => {
    before(async () => {
        await serverReady();
        await closeAll();
    });

    it("runs one of the X-wing's two engine effects in its base pose", async () => {
        await openInPreview(model("Rv_xwing.alo"));
        await inNewestWebview(async () => {
            let engines: Row[] = [];
            await browser.waitUntil(
                async () => {
                    engines = await browser.execute(() => {
                        const v = (window as unknown as { aetDebugViewport?: { treeItems(): Row[] } })
                            .aetDebugViewport;
                        return (v?.treeItems() ?? [])
                            .filter((r) => r.kind === "particle" && /^pe_z95engines/i.test(r.name))
                            .map((r) => ({ kind: r.kind, name: r.name, visible: r.visible, because: r.because }));
                    });
                    return engines.length === 2;
                },
                { timeout: 120_000, interval: 1000, timeoutMsg: "The X-wing's two engine effects did not arrive" },
            );
            await browser.pause(2_000);
            console.log("PRV-48 engines: " + JSON.stringify(engines));

            expect(engines.filter((r) => r.visible)).toHaveLength(1);
            expect(engines.filter((r) => !r.visible).map((r) => r.because)).toEqual(["pose"]);
        });
    });
});
