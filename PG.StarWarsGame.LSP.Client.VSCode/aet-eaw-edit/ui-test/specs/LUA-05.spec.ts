// LUA-05: a broken string is reported once, by the analyzer's parser (Syntax3010), and closing it clears it.
import { expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { diagnosticsWhen, replaceLine } from "./xmlEditor.js";
import { removeScripts, revert, script } from "./luaEditor.js";

const file = script("Miscellaneous", "AetTest_Broken.lua");

describe("LUA-05", () => {
    before(async () => {
        await serverReady();
    });

    it("reports the unterminated string by one parser and clears it once closed", async () => {
        const all = await diagnosticsWhen(file, (d) => d.some((x) => x.code === "aetswg-012-3010"));
        const syntax = all.filter((d) => d.code.startsWith("aetswg-012-"));
        expect(syntax.every((d) => d.code === "aetswg-012-3010")).toBe(true);
        expect(Math.min(...syntax.map((d) => d.line))).toBe(4);
        expect(syntax.filter((d) => d.line === 5 || d.line === 6)).toEqual([]);

        await replaceLine(file, 4, '    local s = "unterminated"');
        await diagnosticsWhen(file, (d) => !d.some((x) => x.code === "aetswg-012-3010"), 60_000, "The syntax errors stay");

        // The reported failure: an error took a while to show. Broken again, it is reported within moments of the
        // edit - about half a second measured; the bound leaves room for a busy machine.
        const broken = Date.now();
        await replaceLine(file, 4, '    local s = "unterminated');
        await diagnosticsWhen(file, (d) => d.some((x) => x.code === "aetswg-012-3010"), 10_000, "The error did not come back");
        expect(Date.now() - broken).toBeLessThan(3_000);
        await revert(file);
    });

    after(() => removeScripts(file));
});
