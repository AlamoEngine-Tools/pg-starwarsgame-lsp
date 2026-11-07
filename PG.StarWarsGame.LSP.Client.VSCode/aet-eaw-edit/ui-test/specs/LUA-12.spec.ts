// LUA-12: rename - a global within its state, a library function across its callers, entry points and engine
// functions refused. Renames are asked for, not applied.
import { expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { codeLenses } from "./xmlEditor.js";
import { hoverWhen, prepareRename, removeScripts, rename, script } from "./luaEditor.js";

const a = script("Miscellaneous", "AetTest_RenameA.lua");
const b = script("Miscellaneous", "AetTest_RenameB.lua");
const interdictor = script("Gameobject", "Interdictor.lua");
const short = (place: string): string => place.replace(/^.*[\\/](\w+[\\/][^\\/]+)$/, "$1").replace(/\\/g, "/");

describe("LUA-12", () => {
    before(async () => {
        await serverReady();
        await hoverWhen(a, { line: 8, text: "Aet_Shared_Helper" }, (c) => c.length > 0);
    });

    it("renames a global within its own state", async () => {
        // The call in the same file; the declaration is not counted (f9b28d72), AetTest_RenameB.lua is another state.
        expect((await codeLenses(a)).find((l) => l.line === 3)?.title).toBe("1 usage");
        const edits = (await rename(a, { line: 8, text: "Aet_Shared_Helper" }, "Aet_Renamed_Helper")).edits.map(short).sort();
        expect(edits).toEqual(["Miscellaneous/AetTest_RenameA.lua:3", "Miscellaneous/AetTest_RenameA.lua:8"]);
    });

    it("renames a library function across its callers", async () => {
        const edits = (await rename(interdictor, { line: 47, text: "Define_State" }, "Aet_Define_State")).edits.map(short);
        expect(edits).toEqual(expect.arrayContaining(["Gameobject/Interdictor.lua:47"]));
        expect(edits.some((e) => e.startsWith("Library/Pgstatemachine.lua:"))).toBe(true);
        expect(new Set(edits.map((e) => e.split(":")[0])).size).toBeGreaterThan(2);
    });

    it("refuses an entry point and an engine function", async () => {
        const story = script("Story", "Story_empire_acti_m02_land.lua");
        expect(await prepareRename(story, { line: 1142, text: "Story_Handle_Esc" })).toMatch(/can't be renamed|cannot be renamed/i);
        const engine = await rename(interdictor, { line: 60, text: "Get_Owner" }, "Aet_Get_Owner");
        expect(engine.edits.filter((e) => /api\.d\.lua/i.test(e))).toEqual([]);
        expect(engine.refused ?? (await prepareRename(interdictor, { line: 60, text: "Get_Owner" })) ?? "").toMatch(
            /Rename refused: It edits api\.d\.lua|can't be renamed|cannot be renamed/i,
        );
    });

    after(() => removeScripts(a, b));
});
