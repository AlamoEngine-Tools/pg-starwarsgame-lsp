// LUA-10: references, the lens and definition stay within the script's own Lua state; stubs are not usages.
import { expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { codeLenses } from "./xmlEditor.js";
import { definition, hoverWhen, references, script } from "./luaEditor.js";

const interdictor = script("Gameobject", "Interdictor.lua");
const short = (place: string): string => place.replace(/^.*[\\/](\w+[\\/][^\\/]+)$/, "$1").replace(/\\/g, "/");

describe("LUA-10", () => {
    before(async () => {
        await serverReady();
        await hoverWhen(interdictor, { line: 112, text: "FindDeadlyEnemy" }, (c) => c.join("\n").includes("---"));
    });

    // The lens leaves the declaration out (f9b28d72); the references list holds it.
    it("counts Definitions' four usages in its own state", async () => {
        expect((await codeLenses(interdictor)).find((l) => l.line === 44)?.title).toBe("4 usages");
        const places = (await references(interdictor, { line: 44, text: "Definitions" })).map(short).sort();
        expect(places).toEqual(
            [
                "Gameobject/Interdictor.lua:44",
                "Library/Pgbasedefinitions.lua:101",
                "Library/Pgbasedefinitions.lua:102",
                "Library/Pgstatemachine.lua:171",
                "Library/Pgstatemachine.lua:172",
            ].sort(),
        );
    });

    it("goes to the script's own State_Init", async () => {
        const places = await definition(interdictor, { line: 47, text: "State_Init", nth: 2 });
        expect(places.map(short)).toEqual(["Gameobject/Interdictor.lua:51"]);
    });
});
