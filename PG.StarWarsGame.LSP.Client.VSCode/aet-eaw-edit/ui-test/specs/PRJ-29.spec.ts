// PRJ-29: the extension shows ONE status bar item while its projects load - "EaWEdit LSP", every project in its
// tooltip. Each server also reports its own startup as work-done progress, and the language client turns that into
// a window progress item of its own; 0.5.1 showed it next to ours as a second footer, "Star Wars LSP - starting".
import { browser, expect } from "@wdio/globals";
import { projectsReady } from "../protocol.js";

/** Records every status bar text that is not ours but names the server, and the most items of ours seen at once. */
async function recordFooters(): Promise<void> {
    await browser.execute(() => {
        const w = window as unknown as {
            __aetFooters: { foreign: string[]; ours: number };
            __aetFooterObserver?: MutationObserver;
        };
        w.__aetFooters = { foreign: [], ours: 0 };
        const read = () => {
            let ours = 0;
            for (const item of Array.from(document.querySelectorAll(".statusbar-item"))) {
                const text = (item.textContent ?? "").trim();
                if (text.startsWith("EaWEdit LSP")) {
                    ours++;
                } else if (text.includes("Star Wars LSP") && !w.__aetFooters.foreign.includes(text)) {
                    w.__aetFooters.foreign.push(text);
                }
            }
            w.__aetFooters.ours = Math.max(w.__aetFooters.ours, ours);
        };
        w.__aetFooterObserver?.disconnect();
        w.__aetFooterObserver = new MutationObserver(read);
        w.__aetFooterObserver.observe(document.body, { subtree: true, childList: true, characterData: true });
        read();
    });
}

describe("PRJ-29", () => {
    it("shows one status item while two projects load", async () => {
        await recordFooters();
        await projectsReady(2);
        const footers = (await browser.execute(
            () => (window as unknown as { __aetFooters: unknown }).__aetFooters,
        )) as { foreign: string[]; ours: number };
        console.log(`PRJ-29 ${JSON.stringify(footers)}`);

        expect(footers.foreign).toEqual([]);
        expect(footers.ours).toBe(1);
    });
});
