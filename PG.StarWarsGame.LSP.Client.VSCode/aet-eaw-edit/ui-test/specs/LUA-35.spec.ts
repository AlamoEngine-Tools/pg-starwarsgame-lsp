// LUA-35: Lua support end to end - the merged hover, the state card, the lens, game object types offered, and
// every finding this server's under an aetswg id.
import { expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { codeLenses, diagnostics, replaceLine } from "./xmlEditor.js";
import { completions, hoverWhen, revert, script } from "./luaEditor.js";

const interdictor = script("Gameobject", "Interdictor.lua");

describe("LUA-35", () => {
    before(async () => {
        await serverReady();
    });

    it("answers in Interdictor.lua end to end", async () => {
        const merged = await hoverWhen(interdictor, { line: 112, text: "FindDeadlyEnemy" }, (c) => c.join("\n").includes("---"));
        expect(merged.length).toBe(1);
        expect(merged[0].split(/\n-{3,}\n/).length).toBe(2);
        expect(
            (await hoverWhen(interdictor, { line: 47, text: '"State_Init"', offset: 2 }, (c) => c.length > 0)).join("\n").replace(/[*`]/g, ""),
        ).toContain("initial state State_Init");
        // The declaration is not counted (f9b28d72).
        expect((await codeLenses(interdictor)).find((l) => l.line === 44)?.title).toBe("4 usages");

        await replaceLine(interdictor, 72, '\t\tmarauder = Find_Object_Type("")');
        const types = await completions(interdictor, { line: 72, text: '("")', offset: 2 });
        expect(types.some((t) => t.label === "Marauder_Missile_Cruiser")).toBe(true);
        await revert(interdictor);

        const all = await diagnostics(interdictor);
        expect(all.every((d) => d.code.startsWith("aetswg-"))).toBe(true);
        const keys = all.map((d) => `${d.line}:${d.message}`);
        expect(new Set(keys).size).toBe(keys.length);
    });
});
