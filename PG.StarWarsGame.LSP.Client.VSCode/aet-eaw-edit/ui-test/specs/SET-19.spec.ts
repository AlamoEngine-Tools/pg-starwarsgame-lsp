// SET-19: two projects in one window - two servers counted in the status, a picker, a channel each.
import { browser, expect } from "@wdio/globals";
import { command, logChannels, lspStatus } from "../protocol.js";

describe("SET-19", () => {
    it("counts two projects indexed", async () => {
        let status: string | null = null;
        await browser.waitUntil(
            async () => (status = await lspStatus()) === "$(check) EaWEdit LSP: 2 projects indexed",
            { timeout: 300_000, interval: 2000, timeoutMsg: `Status ${status}` },
        );
    });

    it("asks which project on Select Project", async () => {
        await command("aet-eaw-edit.lsp.selectProject");
        const labels: string[] = [];
        await browser.waitUntil(
            async () => {
                labels.splice(
                    0,
                    labels.length,
                    ...(await browser.execute(() =>
                        Array.from(document.querySelectorAll(".quick-input-widget .monaco-list-row .label-name")).map(
                            (l) => (l.textContent ?? "").trim(),
                        ),
                    )),
                );
                return labels.length >= 2;
            },
            { timeout: 15_000, timeoutMsg: "No project picker" },
        );
        expect(labels).toEqual(["Empire at War - Vanilla", "Forces of Corruption - Vanilla"]);
        await browser.keys("Escape");
    });

    it("writes a channel per project", () => {
        expect([...logChannels().keys()].sort()).toEqual([
            "Alamo Engine Tools - Empire at War Edit (Empire at War - Vanilla)",
            "Alamo Engine Tools - Empire at War Edit (Forces of Corruption - Vanilla)",
        ]);
    });
});
