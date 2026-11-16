// PRJ-23: an older project file is offered its update, naming its game - Forces of Corruption by
// default. The question itself is modal, and VS Code refuses a modal dialog to an extension under test
// (the prompt fails, measured: "Could not put the project migration to the user"), so answering it
// and the write that follows stay a hand step; what comes before it is read here.
import { browser, expect } from "@wdio/globals";
import { tabLabels } from "../protocol.js";

describe("PRJ-23", () => {
    it("shows the file beside its updated form, naming foc and the current format", async () => {
        let tabs: string[] = [];
        await browser.waitUntil(
            async () => (tabs = await tabLabels()).includes("old-game.pgproj - on disk <-> updated format"),
            { timeout: 120_000, timeoutMsg: `Tabs ${JSON.stringify(tabs)}` },
        );
        // The tab is labelled before its editors are visible, so the proposed text is waited for itself.
        let proposed = "";
        await browser.waitUntil(
            async () => {
                proposed = await browser.executeWorkbench((vscode) => {
                    const editor = vscode.window.visibleTextEditors.find((e: any) => e.document.uri.scheme !== "file");
                    return editor?.document.getText() ?? "";
                });
                return proposed.length > 0;
            },
            { timeout: 30_000, timeoutMsg: "The updated form never showed beside the file" },
        );
        const project = JSON.parse(proposed);
        expect(project.game).toBe("foc");
        expect(project._typeVersion).toBe("aetswg-1.1.0");
    });
});
