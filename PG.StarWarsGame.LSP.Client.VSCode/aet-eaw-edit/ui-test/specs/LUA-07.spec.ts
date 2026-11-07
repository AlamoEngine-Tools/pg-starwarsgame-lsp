// LUA-07: string arguments of engine functions complete from the workspace, enums and the standard library from
// the stubs. Runs on LUA-06's script, copied in from its fixture.
import fs from "node:fs";
import path from "node:path";
import { expect } from "@wdio/globals";
import { protocolDir } from "../paths.js";
import { serverReady } from "../protocol.js";
import { replaceLine } from "./xmlEditor.js";
import { completions, hoverWhen, removeScripts, revert, script } from "./luaEditor.js";

const file = script("Miscellaneous", "AetTest_Players.lua");
const labels = async (line: number, text: string, offset: number): Promise<{ label: string; detail: string }[]> =>
    completions(file, { line, text, offset });

describe("LUA-07", () => {
    before(async () => {
        fs.copyFileSync(
            path.join(protocolDir, "fixtures", "LUA-06", "REPO", "eaw", "Data", "Scripts", "Miscellaneous", "AetTest_Players.lua"),
            file,
        );
        await serverReady();
        await hoverWhen(file, { line: 6, text: "Empire" }, (c) => c.length > 0);
    });

    it("offers the factions inside Find_Player's quotes", async () => {
        await replaceLine(file, 4, '    local me = Find_Player("")');
        const items = await labels(4, '("")', 2);
        expect(items.filter((i) => i.detail === "Faction").map((i) => i.label)).toEqual(
            expect.arrayContaining(["Rebel", "Empire", "Pirates", "Neutral", "Hutts"]),
        );
        await revert(file);
    });

    it("offers music events, text keys and animation names", async () => {
        await replaceLine(file, 7, '    Play_Music("")');
        const music = await labels(7, '("")', 2);
        expect(music.filter((i) => i.detail === "MusicEvent").map((i) => i.label)).toEqual(
            expect.arrayContaining(["Main_Menu_Music_Event", "Test_Music_Event"]),
        );
        await replaceLine(file, 7, '    Game_Message("")');
        const keys = await labels(7, '("")', 2);
        expect(keys.some((i) => i.label.startsWith("TEXT_") && i.detail.length > 0)).toBe(true);
        await replaceLine(file, 7, '    Object.Play_Animation("", false)');
        const clips = (await labels(7, '("",', 2)).map((i) => i.label);
        expect(clips).toEqual(expect.arrayContaining(["Idle", "Move", "Attack", "Die"]));
        await revert(file);
    });

    it("offers Lua 5.0's string library and no math library", async () => {
        await replaceLine(file, 7, "    string.");
        const strings = (await labels(7, "string.", 7)).map((i) => i.label);
        expect(strings).toEqual(expect.arrayContaining(["len", "sub", "format"]));
        await replaceLine(file, 7, "    mat");
        expect((await labels(7, "mat", 3)).map((i) => i.label)).not.toContain("math");
        await revert(file);
    });

    after(() => removeScripts(file));
});
