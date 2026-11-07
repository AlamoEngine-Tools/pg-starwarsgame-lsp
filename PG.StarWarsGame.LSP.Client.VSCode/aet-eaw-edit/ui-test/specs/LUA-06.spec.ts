// LUA-06: Find_Player("local") is no unresolved faction; an unknown one is; a known one hovers as a Faction.
import { expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { diagnosticsWhen } from "./xmlEditor.js";
import { hoverWhen, plain, removeScripts, script } from "./luaEditor.js";

const file = script("Miscellaneous", "AetTest_Players.lua");

describe("LUA-06", () => {
    before(async () => {
        await serverReady();
    });

    it("resolves the factions as the game does", async () => {
        const all = await diagnosticsWhen(file, (d) => d.some((x) => x.code === "aetswg-001-0021"));
        expect(all.filter((d) => d.line === 4)).toEqual([]);
        expect(all.filter((d) => d.line === 6)).toEqual([]);
        const unresolved = all.filter((d) => d.line === 5);
        expect(unresolved.map((d) => [d.code, d.severity, d.message])).toEqual([
            [
                "aetswg-001-0021",
                0,
                "Cannot resolve reference 'Aet_No_Faction': No object with this name exists in the workspace.",
            ],
        ]);
        const cards = await hoverWhen(file, { line: 6, text: "Empire" }, (c) => c.length > 0);
        expect(plain(cards)).toContain('Faction "Empire"');
    });

    after(() => removeScripts(file));
});
