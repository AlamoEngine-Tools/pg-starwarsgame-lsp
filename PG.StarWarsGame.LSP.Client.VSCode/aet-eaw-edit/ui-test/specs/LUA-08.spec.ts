// LUA-08: a plan's task forces are globals; a force without its <Name>_Thread is Engine7.
import { expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { diagnostics, diagnosticsWhen, replaceLine } from "./xmlEditor.js";
import { definition, hoverWhen, revert, script } from "./luaEditor.js";

const file = script("AI", "Raidplan.lua");

describe("LUA-08", () => {
    before(async () => {
        await serverReady();
    });

    it("resolves RaidForce to its TaskForce entry, and wants its thread", async () => {
        await hoverWhen(file, { line: 68, text: "RaidForce" }, (c) => c.length > 0);
        expect((await diagnostics(file)).filter((d) => d.code === "aetswg-011-0007" || /undefined/i.test(d.message))).toEqual([]);
        expect((await definition(file, { line: 68, text: "RaidForce" })).map((l) => l.replace(/^.*[\\/]/, ""))).toEqual([
            "Raidplan.lua:50",
        ]);

        await replaceLine(file, 66, "function RaidForce_ThreadX()");
        const all = await diagnosticsWhen(file, (d) => d.some((x) => x.code === "aetswg-011-0007"), 60_000);
        expect(all.filter((d) => d.code === "aetswg-011-0007").map((d) => [d.line, d.severity, d.message])).toEqual([
            [
                50,
                0,
                "Task force 'RaidForce' has no thread function: The engine starts 'RaidForce_Thread' when the plan loads and cannot run the force without it.",
            ],
        ]);
        await revert(file);
        await diagnosticsWhen(file, (d) => !d.some((x) => x.code === "aetswg-011-0007"), 60_000, "The error stays after the undo");
    });
});
