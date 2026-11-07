// PRV-18: the encyclopedia's Screen field - its default, its tooltip, its eleven entries in order, a redraw per
// pick, and the pick kept when the card is reopened. That it is kept across a window reload stays a hand check
// (VS Code keeps no state under the UI test), as does the Mod Browser's tile (PRV-23).
import { browser, expect } from "@wdio/globals";
import { closeAll, inNewestWebview } from "./storyGraph.js";
import { command, serverReady } from "../protocol.js";

const SCREENS = [
    "800x600",
    "1280x720",
    "1024x768",
    "1366x768",
    "1600x900",
    "1280x1024",
    "1680x1050",
    "1920x1080",
    "1920x1200",
    "2560x1440",
    "3840x2160",
];

const screenField = 'input[aria-label="Screen"]';

/** Opens the Screen list. Run inside the encyclopedia. */
async function openList(): Promise<{ upward: boolean; options: string[] }> {
    await browser.execute((sel: string) => {
        const input = document.querySelector<HTMLInputElement>(sel)!;
        input.blur();
        input.focus();
    }, screenField);
    await (await browser.$('[role="listbox"]')).waitForExist({ timeout: 5_000 });
    return browser.execute(() => {
        const list = document.querySelector('[role="listbox"]')!;
        return {
            upward: list.classList.contains("up"),
            options: Array.from(list.querySelectorAll('[role="option"]')).map((o) => (o.textContent ?? "").trim()),
        };
    });
}

/** Picks a screen from the open list, as a press on it does. Run inside the encyclopedia. */
async function pickScreen(screen: string): Promise<void> {
    await openList();
    await browser.execute((name: string) => {
        const option = Array.from(document.querySelectorAll('[role="option"]')).find(
            (o) => (o.textContent ?? "").trim() === name,
        );
        option?.dispatchEvent(new MouseEvent("mousedown", { bubbles: true, cancelable: true }));
    }, screen);
    await browser.waitUntil(async () => (await (await browser.$(screenField)).getValue()) === screen, {
        timeout: 5_000,
        timeoutMsg: `The field does not read ${screen}`,
    });
    await browser.execute((sel: string) => document.querySelector<HTMLInputElement>(sel)?.blur(), screenField);
}

/** The card's body: the lines of text it is broken into, once drawn. Run inside the encyclopedia. */
async function bodyLines(): Promise<number> {
    await browser.pause(1_500);
    return browser.execute(
        () =>
            Array.from(document.querySelectorAll(".preview-stage div")).filter(
                (d) => d.children.length === 0 && (d.textContent ?? "").trim().length > 0,
            ).length,
    );
}

async function openCard(): Promise<void> {
    await command("aet-eaw-edit.lsp.showEncyclopedia", "Nebulon_B_Frigate");
}

describe("PRV-18", () => {
    before(async () => {
        await serverReady();
    });

    it("offers eleven screens, redraws per pick and keeps the pick", async () => {
        await openCard();
        await inNewestWebview(async () => {
            const field = await browser.$(screenField);
            await field.waitForExist({ timeout: 120_000 });
            expect(await field.getValue()).toBe("1920x1080");
            expect(await (await browser.$("span.select-field")).getAttribute("title")).toBe(
                "The screen resolution the card is drawn for",
            );

            const list = await openList();
            expect(list.options).toEqual(SCREENS);
            expect(list.upward).toBe(true);

            const wide = await bodyLines();
            await pickScreen("800x600");
            const small = await bodyLines();
            expect(small).toBeGreaterThan(wide);
            await pickScreen("3840x2160");
            await pickScreen("1920x1080");
            expect(await bodyLines()).toBe(wide);

            await pickScreen("1280x720");
        });
        await closeAll();

        await openCard();
        await inNewestWebview(async () => {
            const field = await browser.$(screenField);
            await field.waitForExist({ timeout: 120_000 });
            await browser.waitUntil(async () => (await field.getValue()) === "1280x720", {
                timeout: 10_000,
                timeoutMsg: "The reopened card does not read 1280x720",
            });
        });
    });
});
