// LUA-09: a call fitting none of an engine function's call forms is Engine18.
import { expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { diagnosticsWhen } from "./xmlEditor.js";
import { removeScripts, script } from "./luaEditor.js";

const file = script("Miscellaneous", "AetTest_Forms.lua");

describe("LUA-09", () => {
    before(async () => {
        await serverReady();
    });

    it("reports the two calls no form takes", async () => {
        const all = await diagnosticsWhen(file, (d) => d.filter((x) => x.code === "aetswg-011-0018").length >= 2);
        const forms = all.filter((d) => d.code === "aetswg-011-0018");
        expect(forms.map((d) => [d.line, d.severity, d.message])).toEqual([
            [
                4,
                1,
                "No call form of Find_Nearest takes (GameObject, Player): The engine reads the arguments as another form or rejects the call.",
            ],
            [
                7,
                1,
                "No call form of FindDeadlyEnemy takes (GameObject, number): The engine reads the arguments as another form or rejects the call.",
            ],
        ]);
    });

    after(() => removeScripts(file));
});
