// STY-07: the story graph's palette calls a type by its display name (#222). An event's tile carries a glyph
// and the name in words - Time elapsed - with the technical name in its tooltip; the search finds a type by
// either name, and a tile found by its technical name alone shows that name while the search is set. The
// setting aet-eaw-edit.editors.storyTypeNames switches every tile to the technical name, in the open graph.
// The three structural tiles - New event, AND, OR - are the size of a type tile. Before, a tile carried the
// technical name and nothing else, at the dock's shared size.
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { graphLoaded, inNewestWebview, openStoryGraph } from "./storyGraph.js";

const SETTING = "aet-eaw-edit.editors.storyTypeNames";

interface Tile {
    text: string;
    title: string;
    glyph: boolean;
    /** Whether the tile's row is the taller one a technical name needs. */
    technicalRows: boolean;
}

/** A type's tile as drawn, or null while the palette does not show it. Run inside the panel. */
async function tile(type: string): Promise<Tile | null> {
    return browser.execute((name: string) => {
        const el = Array.from(document.querySelectorAll<HTMLElement>(".palette-tile")).find(
            (t) => t.getAttribute("data-type") === name,
        );
        if (!el) return null;
        return {
            text: (el.querySelector(".tile-label")?.textContent ?? "").trim(),
            title: el.getAttribute("title") ?? "",
            glyph: el.querySelector(".tile-glyph svg") !== null,
            technicalRows: el.closest(".palette-scroll")?.classList.contains("technical-names") ?? false,
        };
    }, type);
}

/** The size of every tile in the palette, by what the tile says. Run inside the panel. */
async function sizes(): Promise<Record<string, string>> {
    return browser.execute(() => {
        const out: Record<string, string> = {};
        for (const el of Array.from(document.querySelectorAll<HTMLElement>(".palette-scroll .palette-tile"))) {
            const box = el.getBoundingClientRect();
            out[(el.querySelector(".tile-label")?.textContent ?? "").trim()] = `${Math.round(box.width)}x${Math.round(box.height)}`;
        }
        return out;
    });
}

/** The types the palette shows, in order. Run inside the panel. */
async function shown(): Promise<string[]> {
    return browser.execute(() =>
        Array.from(document.querySelectorAll(".palette-types .palette-tile")).map((t) => t.getAttribute("data-type") ?? ""),
    );
}

/**
 * Replaces the search pattern by keys, as a reader does. Setting an empty value on the field does not
 * reach React, and the pattern of the test before stays in force.
 */
async function search(text: string): Promise<void> {
    const plate = ".palette-scroll .search-plate";
    // The field is there once the magnifier was pressed, or while it holds a pattern.
    if (!(await (await browser.$(`${plate} .search-plate-field`)).isExisting())) {
        const magnifier = await browser.$(`${plate} .search-plate-toggle`);
        await magnifier.waitForExist({ timeout: 30_000 });
        await browser.execute((el: HTMLElement) => el.click(), magnifier as unknown as HTMLElement);
    }
    const field = await browser.$(`${plate} .search-plate-field`);
    await field.waitForExist({ timeout: 30_000 });
    await field.click();
    await browser.keys(["Control", "a"]);
    await browser.keys("Backspace");
    if (text !== "") {
        await field.addValue(text);
    }
    await browser.pause(300);
    const held: string = await browser.execute(
        () => document.querySelector<HTMLInputElement>(".palette-scroll .search-plate-field")?.value ?? "",
    );
    if (held !== text) throw new Error(`The search holds '${held}', not '${text}'`);
}

async function setNames(value: string | undefined): Promise<void> {
    await browser.executeWorkbench(
        (vscode, key: string, to: string | null) =>
            vscode.workspace.getConfiguration().update(key, to ?? undefined, vscode.ConfigurationTarget.Global),
        SETTING,
        value ?? null,
    );
}

describe("STY-07", () => {
    before(async () => {
        await serverReady();
        await setNames(undefined);
        await openStoryGraph("Sandbox_Alderaans_Demise_Rebel", "Rebel");
        await inNewestWebview(async () => {
            await graphLoaded();
            await (await browser.$('button.rotary-pos[title="Edit"]')).click();
            await (await browser.$(".palette-tile")).waitForExist({ timeout: 30_000 });
        });
    });

    after(async () => {
        await setNames(undefined);
    });

    it("shows an event by its display name, with a glyph and the technical name in the tooltip", async () => {
        await inNewestWebview(async () => {
            const elapsed = await tile("STORY_ELAPSED");
            expect(elapsed?.text).toBe("Time elapsed");
            expect(elapsed?.title.split("\n").slice(0, 2)).toEqual(["Time elapsed", "STORY_ELAPSED"]);
            expect(elapsed?.glyph).toBe(true);
            expect(elapsed?.technicalRows).toBe(false);
        });
    });

    it("draws New event, AND and OR at the size of a type tile", async () => {
        await inNewestWebview(async () => {
            const all = await sizes();
            const type = all["Time elapsed"];
            expect(type).toMatch(/^\d+x\d+$/);
            expect({ "New event": all["New event"], AND: all["AND"], OR: all["OR"] }).toEqual({
                "New event": type,
                AND: type,
                OR: type,
            });
        });
    });

    it("finds a type by either name, and shows the technical name where only that matched", async () => {
        await inNewestWebview(async () => {
            await search("time el");
            expect(await shown()).toContain("STORY_ELAPSED");
            expect((await tile("STORY_ELAPSED"))?.text).toBe("Time elapsed");

            // By the technical name alone: the display name is Movie finished.
            await search("movie_done");
            expect(await shown()).toEqual(["STORY_MOVIE_DONE"]);
            const movie = await tile("STORY_MOVIE_DONE");
            expect(movie?.text).toBe("STORY_MOVIE_DONE");
            expect(movie?.title.split("\n").slice(0, 2)).toEqual(["STORY_MOVIE_DONE", "Movie finished"]);

            // A reward, which carries no glyph, by its display name.
            await search("trigger event");
            const trigger = await tile("TRIGGER_EVENT");
            expect(trigger?.text).toBe("Trigger event");
            expect(trigger?.glyph).toBe(false);
            await search("");
        });
    });

    it("switches every tile to the technical name with the setting, in the open graph", async () => {
        await setNames("technical");
        await inNewestWebview(async () => {
            await browser.waitUntil(async () => (await tile("STORY_ELAPSED"))?.text === "STORY_ELAPSED", {
                timeout: 30_000,
                timeoutMsg: "The tiles kept their display names",
            });
            const elapsed = await tile("STORY_ELAPSED");
            expect(elapsed?.title.split("\n").slice(0, 2)).toEqual(["STORY_ELAPSED", "Time elapsed"]);
            expect(elapsed?.technicalRows).toBe(true);
            // Still found by the name it no longer shows.
            await search("time el");
            expect(await shown()).toContain("STORY_ELAPSED");
            await search("");
        });
    });
});
