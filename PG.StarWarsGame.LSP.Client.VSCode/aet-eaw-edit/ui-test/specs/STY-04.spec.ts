// STY-04: a battle's stand-in on the galactic graph says where the battle comes in the order of play,
// the stand-ins are laid out in that order, and the doorway on one opens the battle in Edit mode as it
// does in View (#180). The campaign's two battles are named against their order: the long chain reaches
// Zulu first, and Alpha hangs off a root of its own, which a layout left to itself puts further left.
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { graphLoaded, inNewestWebview, openStoryGraph } from "./storyGraph.js";

interface Stub {
    title: string;
    order: string;
    left: number;
}

/** The battle stand-ins the galactic graph draws, by the battle they stand for. Run inside the panel. */
async function stubs(): Promise<Record<string, Stub>> {
    const found: Stub[] = await browser.execute(() =>
        Array.from(document.querySelectorAll<HTMLElement>('[data-testid="node"].k-TacticalPlot')).map((n) => ({
            title: (n.querySelector(".title")?.textContent ?? "").trim(),
            order: (n.querySelector(".battle-order")?.textContent ?? "").trim(),
            left: n.getBoundingClientRect().left,
        })),
    );
    return Object.fromEntries(found.map((stub) => [/zulu/i.test(stub.title) ? "zulu" : "alpha", stub]));
}

async function bothStubs(): Promise<Record<string, Stub>> {
    await browser.waitUntil(async () => Object.keys(await stubs()).length === 2, {
        timeout: 60_000,
        timeoutMsg: "The galactic graph did not draw both battles",
    });
    return stubs();
}

/** How many story graph panels are open, by their tab titles. */
async function battleTabs(): Promise<string[]> {
    return browser.executeWorkbench((vscode) =>
        vscode.window.tabGroups.all
            .flatMap((group: { tabs: { label: string }[] }) => group.tabs)
            .map((tab: { label: string }) => tab.label),
    );
}

describe("STY-04", () => {
    before(async () => {
        await serverReady();
    });

    it("shows each battle's place in the order of play on its stand-in", async () => {
        await openStoryGraph("Order_Campaign", "Empire");
        await inNewestWebview(async () => {
            await graphLoaded();
            const drawn = await bothStubs();
            expect(drawn.zulu.order).toBe("Battle 1 of 2");
            expect(drawn.alpha.order).toBe("Battle 2 of 2");
        });
    });

    it("lays the stand-ins out in that order, on the first open and after Arrange", async () => {
        await inNewestWebview(async () => {
            const opened = await bothStubs();
            expect(opened.zulu.left).toBeLessThan(opened.alpha.left);

            await (await browser.$('[title="Arrange - recompute the automatic layout"]')).click();
            await browser.pause(3_000);
            const arranged = await bothStubs();
            expect(arranged.zulu.left).toBeLessThan(arranged.alpha.left);
        });
    });

    it("opens a battle through its doorway in Edit mode", async () => {
        const before = await battleTabs();
        await inNewestWebview(async () => {
            await (await browser.$('button.rotary-pos[title="Edit"]')).click();
            await bothStubs();
            const doorway = await browser.$('[data-testid="node"].k-TacticalPlot .jump');
            await doorway.waitForExist({ timeout: 15_000 });
            await doorway.click();
        });
        await browser.waitUntil(async () => (await battleTabs()).length === before.length + 1, {
            timeout: 30_000,
            timeoutMsg: "The doorway opened no battle panel in Edit mode",
        });
    });
});
