// PRV-15: the preview's tooltips and notes name the state and stop. Every title, label and info text the
// shuttle's preview shows in its three lenses and in Scene settings is held to the text rules.
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { model, openInPreview, openSceneSettings } from "./preview.js";
import { inNewestWebview } from "./storyGraph.js";

/** Every title and aria-label on the page, and every info mark's text. Run inside the preview. */
async function texts(): Promise<string[]> {
    const plain: string[] = await browser.execute(() =>
        Array.from(document.querySelectorAll("[title], [aria-label]")).flatMap((e) =>
            [e.getAttribute("title"), e.getAttribute("aria-label")].filter((t): t is string => !!t),
        ),
    );
    const badges: number = await browser.execute(() => document.querySelectorAll(".info-badge").length);
    const tips: string[] = [];
    for (let i = 0; i < badges; i++) {
        await browser.execute((n: number) => document.querySelectorAll<HTMLButtonElement>(".info-badge")[n]?.focus(), i);
        const tip = await browser.$('[role="tooltip"]');
        if (await tip.isExisting()) {
            tips.push((await tip.getText()).trim());
        }
    }
    await browser.execute(() => (document.activeElement as HTMLElement | null)?.blur());
    return [...plain, ...tips];
}

/** What breaks a rule, each with the rule it breaks. */
function breaches(all: string[]): string[] {
    const out: string[] = [];
    for (const text of new Set(all)) {
        // A capital after ": " starts a clause of its own, as the text rules write it.
        const sentences = text.split(/(?<=[.!?:])\s+(?=[A-Z])/).length;
        if (sentences === 1 && text.endsWith(".") && !text.endsWith("...")) {
            out.push(`full stop on one clause: ${text}`);
        }
        if (/\byou(r|rs|rself)?\b/i.test(text)) {
            out.push(`addresses the reader: ${text}`);
        }
        if (/[^\x20-\x7E\n]/.test(text)) {
            out.push(`not ASCII: ${text}`);
        }
        if (/\b\d{3,} (environments|textures|models|objects|particles|types)\b/i.test(text)) {
            out.push(`counts the game's data: ${text}`);
        }
    }
    return out;
}

describe("PRV-15", () => {
    before(async () => {
        await serverReady();
    });

    it("holds every preview tooltip to the text rules", async () => {
        await openInPreview(model("Ev_lambdashuttle.alo"));
        const found: string[] = [];
        await inNewestWebview(async () => {
            await openSceneSettings();
            found.push(...(await texts()));
            for (const lens of ["Model", "Animation", "Gameplay"]) {
                const position = await browser.$(`button.rotary-pos[title^="${lens}"]`);
                if ((await position.isExisting()) && (await position.isEnabled())) {
                    await position.click();
                    await browser.pause(1_500);
                    found.push(...(await texts()));
                }
            }
        });
        expect(found.length).toBeGreaterThan(30);
        expect(breaches(found)).toEqual([]);
    });
});
