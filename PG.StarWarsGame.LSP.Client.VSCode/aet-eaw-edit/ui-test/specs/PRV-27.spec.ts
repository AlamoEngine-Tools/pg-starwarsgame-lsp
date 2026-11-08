// PRV-27: the Schema outline - an enum's kind, its values with the engine's names, a dynamic enum's values
// from the project's file, a behaviour's page and a type's tags. Runs on the schema repository's main (the
// launcher points the schema at it).
import fs from "node:fs";
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { eaw, serverReady } from "../protocol.js";
import { inNewestWebview } from "./storyGraph.js";
import {
    columns,
    openOutline,
    openRow,
    railChildren,
    railClick,
    railExpand,
    rows,
    search,
    selectRow,
    tableReady,
} from "./modBrowser.js";

/** The value names the project's own enum file defines, in its order. */
function enumFile(file: string): string[] {
    const text = fs.readFileSync(path.join(eaw, "Data", "Xml", "Enum", file), "utf8");
    return [...text.matchAll(/<(\w+)>[^<]*<\/\1>/g)].map((m) => m[1]);
}

const bodyText = async (): Promise<string> => browser.execute(() => (document.querySelector("section.preview") as HTMLElement | null)?.innerText ?? "");

describe("PRV-27", () => {
    before(async () => {
        await serverReady();
    });

    it("names an enum's kind and its values' engine names", async () => {
        await openOutline("Schema");
        await inNewestWebview(async () => {
            await tableReady();
            await railClick("Enums");
            await tableReady();
            await search("AnimationType");
            expect((await rows()).find((r) => r.cells[0] === "AnimationType")?.cells[1]).toBe("Built in");
            await search("MovementClassType");
            expect((await rows()).find((r) => r.cells[0] === "MovementClassType")?.cells[1]).toBe("Dynamic");

            await search("AnimationType");
            await selectRow("AnimationType");
            await railExpand("AnimationType", 1);
            await railClick("Values", 2);
            await tableReady();
            expect(await columns()).toEqual(["Name", "Value", "Type", "Origin"]);
            const value = async (name: string): Promise<string | undefined> => {
                await search(name);
                return (await rows()).find((r) => r.cells[0] === name)?.cells[1];
            };
            expect(await value("Transition To Move")).toBe("MOVESTART");
            expect(await value("Choke_Die")).toBe("CHOKEDEATH");
            expect(await value("Choke")).toBe("Choke");
        });
    });

    it("lists a dynamic enum's values from the project's file, each with its users", async () => {
        await openOutline("Schema");
        await inNewestWebview(async () => {
            await tableReady();
            await railClick("Enums");
            await tableReady();
            await search("MovementClassType");
            await selectRow("MovementClassType");
            await railExpand("MovementClassType", 1);
            await railClick("Values", 2);
            await tableReady();
            await search("");
            // Listed by name, not in the file's order.
            expect((await rows()).map((r) => r.cells[0]).sort()).toEqual(enumFile("Movementclasstype.xml").sort());

            await selectRow("Infantry");
            await railExpand("Infantry", 3);
            await railExpand("References", 4);
            expect((await railChildren("References")).length).toBeGreaterThan(0);
        });
    });

    it("shows what decides a behaviour and a type's tags", async () => {
        await openOutline("Schema");
        await inNewestWebview(async () => {
            await tableReady();
            await railClick("Behaviours");
            await tableReady();
            await search("Planet");
            await openRow("Planet");
            await browser.waitUntil(async () => (await bodyText()).includes("PLANET"), {
                timeout: 30_000,
                timeoutMsg: "The Planet page does not name the PLANET behaviour",
            });
            expect(await bodyText()).toContain("Coruscant");

            await railClick("Types");
            await tableReady();
            await search("GameObjectType");
            await selectRow("GameObjectType");
            await railExpand("GameObjectType", 1);
            // A type holds its tags under one relation.
            const relations = await railChildren("GameObjectType");
            expect(relations.length).toBe(1);
            await railExpand(relations[0].label, 2);
            const tags = await railChildren(relations[0].label);
            expect(tags.length).toBeGreaterThan(10);
            await openRow("GameObjectType");
            await railClick(tags[0].label, 3);
            await browser.keys("Enter");
            await browser.waitUntil(async () => /value type/i.test(await bodyText()), {
                timeout: 30_000,
                timeoutMsg: `The tag ${tags[0].label} shows no documentation`,
            });
        });
    });
});
