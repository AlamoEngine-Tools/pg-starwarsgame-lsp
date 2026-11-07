// LUA-15: types per script - another script's global does not type this one; library and stub types do.
import { expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { completions, hoverWhen, insertLine, removeScripts, revert, script } from "./luaEditor.js";

const a = script("Miscellaneous", "AetTest_TypeA.lua");
const b = script("Miscellaneous", "AetTest_TypeB.lua");

describe("LUA-15", () => {
    before(async () => {
        await serverReady();
        await hoverWhen(a, { line: 4, text: "aet_shared_value" }, (c) => c.length > 0);
    });

    it("types this script's values from this script, the library and the stubs", async () => {
        const copy = (await hoverWhen(b, { line: 9, text: "copy" }, (c) => c.length > 0)).join("\n");
        expect(copy).toMatch(/number|integer/);
        expect(copy).not.toMatch(/\bstring\b/);
        expect((await hoverWhen(b, { line: 10, text: "ok" }, (c) => c.length > 0)).join("\n")).toMatch(/boolean/);

        const line = await insertLine(b, 11, "    GlobalValue.");
        expect((await completions(b, { line, text: "GlobalValue.", offset: 12 })).map((c) => c.label)).toEqual(
            expect.arrayContaining(["Get", "Set"]),
        );
        await revert(b);
        await insertLine(b, 11, "    ThreadValue.");
        expect((await completions(b, { line, text: "ThreadValue.", offset: 12 })).map((c) => c.label)).toEqual(
            expect.arrayContaining(["Get", "Set", "Reset"]),
        );
        await revert(b);
    });

    after(() => removeScripts(a, b));
});
