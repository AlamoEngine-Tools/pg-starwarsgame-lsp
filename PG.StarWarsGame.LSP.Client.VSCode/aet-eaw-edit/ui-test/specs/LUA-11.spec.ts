// LUA-11: from a library, definition and references reach every script requiring it.
import { expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { definition, hoverWhen, references, script } from "./luaEditor.js";

const library = script("Library", "Pgbasedefinitions.lua");
const folder = (place: string): string => place.split(/[\\/]/).slice(-2, -1)[0].toLowerCase();

describe("LUA-11", () => {
    before(async () => {
        await serverReady();
        await hoverWhen(library, { line: 102, text: "Definitions" }, (c) => c.length > 0);
    });

    it("reaches every script's Definitions", async () => {
        const places = await definition(library, { line: 102, text: "Definitions" });
        expect(places.length).toBeGreaterThan(5);
        expect(places.some((p) => /Gameobject[\\/]Interdictor\.lua:44$/i.test(p))).toBe(true);
        expect(places.some((p) => folder(p) === "story")).toBe(true);

        const uses = await references(library, { line: 102, text: "Definitions" });
        const folders = new Set(uses.map(folder));
        expect([...folders]).toEqual(expect.arrayContaining(["story", "gameobject", "ai"]));
    });
});
