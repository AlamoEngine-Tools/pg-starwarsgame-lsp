// The story graph panel, as the SET tests read it.
import { browser } from "@wdio/globals";
import { command } from "../protocol.js";

/**
 * Runs a read inside the newest webview panel, then leaves it. The toasts are hidden first - kept in
 * the notification centre - because one over the panel takes a pointer click meant for it.
 */
export async function inNewestWebview<T>(read: () => Promise<T>, timeout = 90_000): Promise<T> {
    const workbench = await browser.getWorkbench();
    let views: Awaited<ReturnType<typeof workbench.getAllWebviews>> = [];
    await browser.waitUntil(async () => (views = await workbench.getAllWebviews()).length > 0, {
        timeout,
        timeoutMsg: "No webview panel opened",
    });
    await browser.executeWorkbench((vscode) => vscode.commands.executeCommand("notifications.hideToasts"));
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

/**
 * The modes the story graph's switch offers, as their tooltips name them; a disabled one says why. The
 * switch renders even for a campaign the game never loads, so the graph's own status is checked first.
 */
export async function storyModes(campaign: string, faction: string): Promise<string[]> {
    await openStoryGraph(campaign, faction);
    return inNewestWebview(async () => {
        const position = await browser.$("button.rotary-pos");
        await position.waitForExist({ timeout: 90_000 });
        await graphLoaded();
        return browser.execute(() =>
            Array.from(document.querySelectorAll("button.rotary-pos")).map((b) => b.getAttribute("title") ?? ""),
        );
    });
}

/** Waits until the graph has drawn its events, failing on a campaign it reports missing. Run inside the panel. */
export async function graphLoaded(timeout = 120_000): Promise<void> {
    await browser.waitUntil(
        async () => {
            const status: string = await browser.execute(() => document.querySelector("p.status")?.textContent ?? "");
            if (/was not found/.test(status)) throw new Error(`The story graph says: ${status}`);
            return browser.execute(() => document.querySelector(".minimap") !== null && !document.querySelector(".minimap-empty"));
        },
        { timeout, timeoutMsg: "The story graph drew nothing" },
    );
}

/** Closes every editor and panel. */
export async function closeAll(): Promise<void> {
    await browser.executeWorkbench((vscode) => vscode.commands.executeCommand("workbench.action.closeAllEditors"));
}
