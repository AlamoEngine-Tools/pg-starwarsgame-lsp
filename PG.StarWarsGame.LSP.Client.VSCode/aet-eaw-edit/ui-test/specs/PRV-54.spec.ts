// PRV-54 (#394): a particle file's skipped emitter property is ONE note per property per file, named as the
// setting it is and saying it has no effect in game - not a line per emitter, repeated for every proxy that
// uses the file. The Empire level-1 station, Eb_station_01.alo, hangs p_rb_station_damage on its damage
// proxies; the file's three emitters each carry property 0x06, which the game reads past. Before, the notes
// read "Emitter 0 - property 0x06, which the game reads past", once per emitter and once per proxy name.
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { model, openInPreview, press, type Note } from "./preview.js";
import { closeAll, inNewestWebview } from "./storyGraph.js";

/** The notes once the severity tag has held the same count for five seconds. Run inside the preview. */
async function settledNotes(): Promise<Note[]> {
    let last = "";
    let since = Date.now();
    await browser.waitUntil(
        async () => {
            const tag = await browser.execute(() =>
                Array.from(document.querySelectorAll(".validate-btn"))
                    .map((b) => b.getAttribute("title") ?? "")
                    .join(" | "),
            );
            if (tag !== last) {
                last = tag;
                since = Date.now();
            }
            return /^Read \d+ notes?$/.test(tag) && Date.now() - since > 5_000;
        },
        { timeout: 180_000, interval: 500, timeoutMsg: `The notes never settled; the tag said '${last}'` },
    );
    await press(".validate-btn");
    await browser.$(".preview-problems .problem-row").waitForExist({ timeout: 10_000 });
    const rows = await browser.execute(() =>
        Array.from(document.querySelectorAll(".preview-problems .problem-row")).map((row) => ({
            tone: (row.querySelector('[class*="sev-"]')?.className.match(/sev-(\w+)/) ?? [])[1] ?? "",
            text: ((row as HTMLElement).innerText ?? "").replace(/\s+/g, " ").trim(),
        })),
    );
    return rows.map((row) => {
        const id = / (aetswg-\d{3}-\d{4})$/.exec(row.text)?.[1] ?? "";
        return { tone: row.tone, id, text: id === "" ? row.text : row.text.slice(0, -(id.length + 1)) };
    });
}

describe("PRV-54", () => {
    before(async () => {
        await serverReady();
        await closeAll();
    });

    it("says once that p_rb_station_damage's property 0x06 has no effect, for all three emitters", async () => {
        await openInPreview(model("Eb_station_01.alo"));
        await inNewestWebview(async () => {
            const notes = await settledNotes();
            console.log("PRV-54 notes: " + JSON.stringify(notes));

            const station = notes.filter((n) => /p_rb_station_damage/i.test(n.text));
            expect(station).toHaveLength(1);
            expect(station[0].tone).toBe("info");
            expect(station[0].id).toBe("aetswg-014-0023");
            expect(station[0].text).toMatch(
                /: Emitters 0-2 - property 0x06 is read past by the game and has no effect$/,
            );
        });
    });

    // The dock names the same file through two damage-stage proxies, p_rb_station_damage_ALT1 and _ALT2.
    // Both resolve to one file, so the note is still said once.
    it("says it once for Eb_dock00, whose two damage stages name the same file", async () => {
        await closeAll();
        await openInPreview(model("Eb_dock00.alo"));
        await inNewestWebview(async () => {
            const notes = await settledNotes();
            console.log("PRV-54 dock notes: " + JSON.stringify(notes));

            const station = notes.filter((n) => /p_rb_station_damage/i.test(n.text));
            expect(station).toHaveLength(1);
            expect(station[0].text).toMatch(
                /: Emitters 0-2 - property 0x06 is read past by the game and has no effect$/,
            );
        });
    });
});
