// LUA-36: a require of a script the game ships is no missing module. The game packs its Lua libraries into
// its archives, so a mod that requires `PGStateMachine` carries no copy of it, and the game loads it all the
// same. The fixture mod sits on the game alone and holds one script with two requires: the shipped library,
// which reports nothing, and a module nobody ships, which is still an error.
import { expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { diagnosticsWhen } from "./xmlEditor.js";

const shipped = "C:\\temp\\aet-test-lua36\\Data\\Scripts\\Story\\AetShipped.lua";

describe("LUA-36", () => {
    before(async () => {
        await serverReady();
    });

    it("reports a module nobody ships and not one the game ships", async () => {
        // The module nobody ships is the sign that the script's requires have been checked at all.
        const all = await diagnosticsWhen(
            shipped,
            (found) => found.some((d) => d.message.includes("Aet_No_Such_Module")),
            120_000,
            "The script's requires were never checked",
        );
        console.log("LUA-36 findings: " + JSON.stringify(all));

        const missing = all.filter((d) => d.message.includes("Aet_No_Such_Module"));
        expect(missing.length).toBe(1);
        expect(missing[0].line).toBe(2);
        expect(missing[0].severity).toBe(0);
        expect(missing[0].code.startsWith("aetswg-")).toBe(true);

        expect(all.filter((d) => d.message.includes("PGStateMachine"))).toEqual([]);
    });
});
