// The story graph panel, as the SET tests read it.
import { browser } from "@wdio/globals";
import { command } from "../protocol.js";

/** Runs a read inside the newest webview panel, then leaves it. */
export async function inNewestWebview<T>(read: () => Promise<T>, timeout = 90_000): Promise<T> {
    const workbench = await browser.getWorkbench();
    let views: Awaited<ReturnType<typeof workbench.getAllWebviews>> = [];
    await browser.waitUntil(async () => (views = await workbench.getAllWebviews()).length > 0, {
        timeout,
        timeoutMsg: "No webview panel opened",
    });
    const view = views[views.length - 1];
    await view.open();
    try {
        return await read();
    } finally {
        await view.close();
    }
}

/** Opens a campaign's story graph for a faction. */
export async function openStoryGraph(campaign: string, faction: string): Promise<void> {
    await command("aet-eaw-edit.lsp.openStoryGraph", { campaignName: campaign, factionName: faction });
}

/** The modes the story graph's switch offers, as their tooltips name them; a disabled one says why. */
export async function storyModes(campaign: string, faction: string): Promise<string[]> {
    await openStoryGraph(campaign, faction);
    return inNewestWebview(async () => {
        const position = await browser.$("button.rotary-pos");
        await position.waitForExist({ timeout: 90_000 });
        return browser.execute(() =>
            Array.from(document.querySelectorAll("button.rotary-pos")).map((b) => b.getAttribute("title") ?? ""),
        );
    });
}

/** Closes every editor and panel. */
export async function closeAll(): Promise<void> {
    await browser.executeWorkbench((vscode) => vscode.commands.executeCommand("workbench.action.closeAllEditors"));
}
