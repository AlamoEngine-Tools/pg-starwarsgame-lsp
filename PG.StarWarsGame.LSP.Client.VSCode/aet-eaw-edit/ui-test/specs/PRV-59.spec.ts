// PRV-59 (#420): textures are looked up and decoded as the game does. The game opens a texture name as written
// whatever its extension, and when that file does not exist it opens the name up to its FIRST dot with .dds
// appended - nothing else. The file found is decoded by its content, so a PNG loads like a TGA or a DDS, under
// any extension. The fixture's models are the waypoint beacon twice with renamed textures: Prv_png.alo names a
// PNG and a .dds of which only the .tga exists, Prv_mislabel.alo names a .tga holding a PNG. Its XML names a
// PNG, the .dds backed only by a .tga, and a two-dot name only its first-dot .dds satisfies.
// It also names Mt_commandbar.tga, which the game ships, beside a generated Mt_commandbar.dds of the project: the
// game looks for the name as written in mod folders, loose files and archives before it tries the .dds, so the
// project's file is not the one opened, and a warning says so.
// Before, a .png reference was a format error and not in the asset index, a model's PNG drew untextured, a PNG
// under a .tga name could not be decoded, a .dds name was satisfied by the .tga the game never opens, and a
// two-dot name was cut at its last dot.
import fs from "node:fs";
import { browser, expect } from "@wdio/globals";
import { hoverWhenReady, serverReady } from "../protocol.js";
import {
    openOutline,
    railClick,
    rows,
    search,
    tableReady,
} from "./modBrowser.js";
import {
    type Note,
    openInPreview,
    openSceneSettings,
    pickRenderer,
    previewNotesWhen,
    textureSampling,
} from "./preview.js";
import { closeAll, inNewestWebview } from "./storyGraph.js";
import { diagnosticsWhen } from "./xmlEditor.js";

const ROOT = "C:\\temp\\aet-test-PRV-png";
const UNITS = `${ROOT}\\Data\\XML\\PngUnits.xml`;
const PNG_MODEL = `${ROOT}\\Data\\Art\\Models\\Prv_png.alo`;
const MISLABELLED_MODEL = `${ROOT}\\Data\\Art\\Models\\Prv_mislabel.alo`;

const TEXTURE_MISSING = "aetswg-004-0009";
const TEXTURE_FORMAT = "aetswg-004-0010";
const MODEL_TEXTURE_MISSING = "aetswg-004-0012";
const TEXTURE_IS_PNG = "aetswg-004-0022";
const TEXTURE_DDS_NOT_REACHED = "aetswg-004-0023";
const WARNING = 1;
const INFORMATION = 2;
/** The generated textures' edge, in texels. */
const SIZE = 128;

/** The 1-based line of the fixture's XML holding `text`, and the 0-based column it starts at. */
function where(text: string): { line: number; column: number } {
    const lines = fs.readFileSync(UNITS, "utf8").split(/\r?\n/);
    const index = lines.findIndex(
        (l) => l.includes(text) && !l.includes("<!--"),
    );
    return { line: index + 1, column: lines[index].indexOf(text) };
}

/** Waits for the model's parts to arrive. Run inside the preview. */
async function loaded(): Promise<void> {
    await browser.waitUntil(
        async () =>
            browser.execute(() => {
                const v = (
                    window as unknown as {
                        aetDebugViewport?: { parts: Map<string, unknown> };
                    }
                ).aetDebugViewport;
                return v !== undefined && v.parts.size > 0;
            }),
        { timeout: 120_000, timeoutMsg: "The model did not load" },
    );
    await browser.pause(3_000);
}

/** Whether the viewport holds a texture by this lower-case name. Run inside the preview. */
async function holds(name: string): Promise<boolean> {
    return browser.execute(
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        (wanted: string) =>
            (window as any).aetDebugViewport.textures.get(wanted) !== undefined,
        name,
    );
}

const named = (notes: Note[], tone: string, pattern: RegExp): Note[] =>
    notes.filter((n) => n.tone === tone && pattern.test(n.text));

describe("PRV-59", () => {
    before(async () => {
        await serverReady();
        await closeAll();
    });

    it("resolves a PNG texture reference with a note recommending DDS, and looks a name up as the game does", async () => {
        const model = where("Prv_png.alo");
        const mislabelled = where("Prv_mislabel.alo");
        const icon = where("Prv59_Icon.png");
        const range = where("Prv59_DdsOnly.dds");
        const twoDots = where("Prv59_Two.dots.tga");
        const shadowed = where("Mt_commandbar.tga");

        const all = await diagnosticsWhen(UNITS, (d) =>
            d.some((x) => x.line === model.line),
        );
        console.log("PRV-59 XML findings: " + JSON.stringify(all));
        const on = (line: number): typeof all =>
            all.filter((x) => x.line === line);

        // A PNG named as one: no format error, no missing file, one information.
        expect(on(icon.line).map((x) => x.code)).toEqual([TEXTURE_IS_PNG]);
        expect(on(icon.line)[0].severity).toBe(INFORMATION);
        expect(on(icon.line)[0].message).toMatch(/Prv59_Icon\.png/);
        expect(on(icon.line)[0].message).toMatch(/DDS/);

        // A .dds name with only the .tga on disk: missing, saying the game never looks for the .tga.
        expect(on(range.line).map((x) => x.code)).toEqual([TEXTURE_MISSING]);
        expect(on(range.line)[0].severity).toBe(WARNING);
        expect(on(range.line)[0].message).toMatch(/Prv59_DdsOnly\.tga/);

        // Cut at the first dot: Prv59_Two.dds satisfies it.
        expect(on(twoDots.line)).toEqual([]);

        // The name as written exists in the game, so the project's .dds of that stem is never opened.
        expect(on(shadowed.line).map((x) => x.code)).toEqual([
            TEXTURE_DDS_NOT_REACHED,
        ]);
        expect(on(shadowed.line)[0].severity).toBe(WARNING);
        expect(on(shadowed.line)[0].message).toMatch(/mt_commandbar\.tga/);
        expect(on(shadowed.line)[0].message).toMatch(/mt_commandbar\.dds/);

        // The model's own textures follow the same rule.
        const inModel = on(model.line);
        expect(
            inModel.filter(
                (x) =>
                    x.code === MODEL_TEXTURE_MISSING &&
                    /Prv59_HullP\.png/.test(x.message),
            ),
        ).toEqual([]);
        const ddsOnly = inModel.filter(
            (x) =>
                x.code === MODEL_TEXTURE_MISSING &&
                /Prv59_DdsOnly\.dds/.test(x.message),
        );
        expect(ddsOnly.length).toBe(1);
        expect(ddsOnly[0].message).toMatch(/Prv59_DdsOnly\.tga/);
        const png = inModel.filter((x) => x.code === TEXTURE_IS_PNG);
        expect(png.map((x) => x.severity)).toEqual([INFORMATION]);
        expect(png[0].message).toMatch(/Prv59_HullP\.png/);
        expect(
            on(mislabelled.line).filter(
                (x) => x.code === MODEL_TEXTURE_MISSING,
            ),
        ).toEqual([]);
        expect(all.filter((x) => x.code === TEXTURE_FORMAT)).toEqual([]);

        const hover = await hoverWhenReady(
            UNITS,
            icon.line - 1,
            icon.column + 2,
        );
        expect(hover.toLowerCase()).toContain(
            "data/art/textures/prv59_icon.png",
        );
    });

    it("draws a PNG texture filtered and mipmapped like a TGA in both renderers, and a .dds backed only by a .tga as missing", async () => {
        await openInPreview(PNG_MODEL);
        await inNewestWebview(async () => {
            await loaded();
            await openSceneSettings();
            const levels = Math.floor(Math.log2(SIZE)) + 1;

            await pickRenderer("Default");
            const plain = await textureSampling("prv59_hullp.png");
            console.log("PRV-59 PNG under Default: " + JSON.stringify(plain));
            expect(plain.minFilter).toBe("LINEAR_MIPMAP_LINEAR");
            expect(plain.magFilter).toBe("LINEAR");
            expect(plain.levels).toBe(levels);

            await pickRenderer("Game");
            const game = await textureSampling("prv59_hullp.png");
            console.log("PRV-59 PNG under Game: " + JSON.stringify(game));
            expect(game.minFilter).toBe("LINEAR_MIPMAP_LINEAR");
            expect(game.magFilter).toBe("LINEAR");
            expect(game.levels).toBe(levels);

            expect(await holds("prv59_ddsonly.dds")).toBe(false);
            const notes = await previewNotesWhen(
                (n) =>
                    named(n, "info", /Prv59_HullP\.png/).length > 0 &&
                    named(n, "warning", /Prv59_DdsOnly\.dds/).length > 0,
                "The notes never named the PNG and the missing .dds",
            );
            console.log(
                "PRV-59 notes of Prv_png.alo: " + JSON.stringify(notes),
            );
            expect(named(notes, "info", /Prv59_HullP\.png/)[0].text).toMatch(
                /DDS/,
            );
            expect(
                named(notes, "warning", /Prv59_DdsOnly\.dds/)[0].text,
            ).toMatch(/Prv59_DdsOnly\.tga/);
            expect(named(notes, "warning", /Prv59_HullP\.png/)).toEqual([]);
        });
    });

    it("draws a PNG saved under a .tga name, with a note on the mismatch", async () => {
        await closeAll();
        await openInPreview(MISLABELLED_MODEL);
        await inNewestWebview(async () => {
            await loaded();
            const sampled = await textureSampling("prv59_mislb.tga");
            console.log(
                "PRV-59 PNG under a .tga name: " + JSON.stringify(sampled),
            );
            expect(sampled.minFilter).toBe("LINEAR_MIPMAP_LINEAR");
            expect(sampled.levels).toBe(Math.floor(Math.log2(SIZE)) + 1);

            const notes = await previewNotesWhen(
                (n) => named(n, "info", /Prv59_Mislb\.tga/).length > 0,
                "The notes never named the mislabelled texture",
            );
            console.log(
                "PRV-59 notes of Prv_mislabel.alo: " + JSON.stringify(notes),
            );
            expect(named(notes, "info", /Prv59_Mislb\.tga/)[0].text).toMatch(
                /PNG/,
            );
            expect(named(notes, "warning", /Prv59_Mislb\.tga/)).toEqual([]);
        });
    });

    it("lists PNG textures in the Mod Browser", async () => {
        await closeAll();
        await openOutline("Assets");
        await inNewestWebview(async () => {
            await tableReady();
            await railClick("Textures");
            await tableReady();
            await search("Prv59");
            const names = (await rows())
                .map((r) => r.cells[0].toLowerCase())
                .sort();
            console.log(
                "PRV-59 Mod Browser textures: " + JSON.stringify(names),
            );
            expect(names).toEqual(
                expect.arrayContaining([
                    "prv59_hullp.png",
                    "prv59_icon.png",
                    "prv59_mislb.tga",
                    "prv59_two.dds",
                ]),
            );
        });
    });
});
