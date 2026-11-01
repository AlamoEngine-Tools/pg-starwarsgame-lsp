// LUA-04: the analyzer's findings are folded under LuaAnalyzer ids; type rules are dropped; a surplus argument kept.
import { expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { diagnostics, diagnosticsWhen } from "./xmlEditor.js";
import { hoverWhen, removeScripts, script } from "./luaEditor.js";

const file = script("Miscellaneous", "AetTest_Rules.lua");

describe("LUA-04", () => {
    before(async () => {
        await serverReady();
    });

    it("publishes the analyzer's findings under aetswg ids", async () => {
        // The analyzer's findings arrive over a few publishes: waited for until every expected one is in. Line
        // 16's iter-variable-reassign is not among them: emmylua_ls 0.25.1 does not report it (measured).
        const expected: [number, string][] = [
            [7, "aetswg-016-0009"],
            [14, "aetswg-016-0006"],
            [19, "aetswg-016-0002"],
            [20, "aetswg-016-0011"],
        ];
        let all = await diagnostics(file);
        try {
            all = await diagnosticsWhen(file, (d) => expected.every(([line, code]) => d.some((x) => x.line === line && x.code === code)), 90_000);
        } catch {
            throw new Error(`Not every finding arrived: ${JSON.stringify((await diagnostics(file)).map((d) => `${d.line}:${d.code}:${d.severity}:${d.message}`))}`);
        }
        const at = (line: number): string[] => all.filter((d) => d.line === line).map((d) => d.code);
        expect(at(7)).toContain("aetswg-016-0009");
        expect(at(14)).toContain("aetswg-016-0006");
        expect(at(19)).toContain("aetswg-016-0002");
        expect(all.find((d) => d.line === 20 && d.code === "aetswg-016-0011")?.severity).toBe(1);
        // Sleep's arity is not published; its missing require is - the fixture requires no PGBase.
        expect(all.filter((d) => d.line === 21).map((d) => [d.code, d.message])).toEqual([
            ["aetswg-006-0019", "Global 'Sleep' is defined in 'Pgbase' which is not required."],
        ]);
        expect(at(23)).toEqual([]);
        expect(all.every((d) => d.code.startsWith("aetswg-"))).toBe(true);
        expect(all.filter((d) => /undefined-field|param-type-mismatch|missing-parameter|need-check-nil/.test(d.code))).toEqual([]);

        // A hint is not listed in the Problems view; the hover shows it.
        const unused = (await diagnostics(file)).find((d) => d.line === 13);
        expect(unused?.code).toBe("aetswg-016-0001");
        expect(unused?.severity).toBe(3);
        await hoverWhen(file, { line: 13, text: "unused_value" }, (c) => c.length > 0, 30_000);
    });

    after(() => removeScripts(file));
});
