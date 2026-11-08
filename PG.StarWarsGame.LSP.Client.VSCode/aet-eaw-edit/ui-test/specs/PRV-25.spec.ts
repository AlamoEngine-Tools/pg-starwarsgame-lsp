// PRV-25: the Assets outline - a model's relations and page, an animation previewed on its model, a particle
// system's references, and Reveal in File Explorer on a packed and a loose texture. Names are matched without
// case; PRV-16 holds their spelling.
import { browser, expect } from "@wdio/globals";
import { serverReady, tabLabels } from "../protocol.js";
import { closeAll, inNewestWebview } from "./storyGraph.js";
import {
    actions,
    contextMenu,
    entryHead,
    openOutline,
    openRow,
    panels,
    railChildren,
    railClick,
    railExpand,
    railMenuPick,
    rowActions,
    rows,
    search,
    selectRow,
    tableReady,
} from "./modBrowser.js";

const lower = (names: string[]): string[] => names.map((n) => n.toLowerCase());

describe("PRV-25", () => {
    before(async () => {
        await serverReady();
    });

    it("shows the shuttle's relations and page", async () => {
        await openOutline("Assets");
        await inNewestWebview(async () => {
            await tableReady();
            await railClick("Models");
            await tableReady();
            await search("Ev_lambdashuttle");
            await selectRow("Ev_lambdashuttle.alo");
            await railExpand("Ev_lambdashuttle.alo", 1);
            expect((await railChildren("Ev_lambdashuttle.alo")).map((n) => n.label)).toEqual([
                "Animations",
                "Particle Systems",
                "Textures",
                "Referenced by",
            ]);
            await railExpand("Animations", 2);
            expect(lower((await railChildren("Animations")).map((n) => n.label))).toEqual([
                "ev_lambdashuttle_idle_00.ala",
                "ev_lambdashuttle_land_00.ala",
                "ev_lambdashuttle_takeoff_00.ala",
            ]);

            await openRow("Ev_lambdashuttle.alo");
            await browser.waitUntil(async () => (await panels()).includes("Model tree"), { timeout: 60_000 });
            const shown = (await panels()).filter((p) => p !== "Problems");
            expect(shown).toEqual(["Header", "Model tree", "Textures", "Particle Systems", "Animations", "Referenced by"]);
            expect(Object.keys((await entryHead()).rows)).toEqual(
                expect.arrayContaining(["Bones", "Textures", "Particle Systems", "Animations"]),
            );

            await railMenuPick("Ev_lambdashuttle_land_00.ala", "Preview on its model");
        });
        // The preview is titled with the clip it opened on, and draws the clip's model.
        await browser.waitUntil(async () => (await tabLabels()).some((t) => /^ev_lambdashuttle_land_00\.ala/i.test(t)), {
            timeout: 60_000,
            timeoutMsg: "Preview on its model opened no preview of the land clip",
        });
        await inNewestWebview(async () => {
            await browser.waitUntil(
                async () =>
                    browser.execute(() =>
                        Array.from(document.querySelectorAll(".bone-tree .tree-name")).some((n) => n.textContent === "EV_LambdaShuttle_ALT0"),
                    ),
                { timeout: 60_000, timeoutMsg: "The clip's preview does not draw the shuttle" },
            );
        });
        // The next test reads the Mod Browser as the newest panel.
        await closeAll();
    });

    it("lists the model carrying a particle system among its references", async () => {
        await openOutline("Assets");
        await inNewestWebview(async () => {
            await tableReady();
            await railClick("Particle Systems");
            await tableReady();
            await search("P_sandspeederheat");
            await selectRow("P_sandspeederheat.alo");
            await railExpand("P_sandspeederheat.alo", 1);
            await railExpand("Referenced by", 2);
            expect(lower((await railChildren("Referenced by")).map((n) => n.label))).toContain("nv_sandspeeder.alo");

            await openRow("P_sandspeederheat.alo");
            const tiles = await actions();
            expect(tiles.find((t) => t.label === "Preview model")?.disabled).toBe(false);
            expect(tiles.find((t) => t.label === "Go to definition")).toMatchObject({ disabled: true, title: "Not a text file" });
        });
    });

    it("reveals a loose texture and not a packed one", async () => {
        await openOutline("Assets");
        await inNewestWebview(async () => {
            await tableReady();
            await railClick("Textures");
            await tableReady();
            await search("", [], ["Game"]);
            const packed = (await rows())[0].cells[0];
            // The menu lists only what can be done; the row's own action says why it cannot.
            expect((await contextMenu(packed)).map((m) => m.label)).not.toContain("Reveal in File Explorer");
            expect((await rowActions(packed)).find((a) => a.title.startsWith("Reveal in File Explorer"))).toEqual({
                disabled: true,
                title: "Reveal in File Explorer - Packed in the game's archives",
            });

            await search("");
            const loose = (await rows()).find((r) => r.cells[3] !== "Game")!.cells[0];
            expect((await contextMenu(loose)).find((m) => m.label === "Reveal in File Explorer")?.disabled).toBe(false);
        });
    });
});
