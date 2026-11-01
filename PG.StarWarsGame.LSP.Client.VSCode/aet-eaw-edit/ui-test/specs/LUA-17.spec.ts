// LUA-17: the state machine's findings Engine8, 9, 11, 12, 13 and the state names offered in Set_Next_State.
import { expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { diagnosticsWhen, replaceLine } from "./xmlEditor.js";
import { completions, removeScripts, revert, script } from "./luaEditor.js";

const file = script("Gameobject", "AetTest_Machine.lua");

describe("LUA-17", () => {
    before(async () => {
        await serverReady();
    });

    it("reports the machine's mistakes", async () => {
        const all = await diagnosticsWhen(file, (d) => d.some((x) => x.code === "aetswg-011-0012"));
        const finding = (code: string): [number, number, string][] =>
            all.filter((d) => d.code === code).map((d) => [d.line, d.severity, d.message]);
        expect(finding("aetswg-011-0011")).toEqual([
            [7, 1, "State 'State_Done' is registered again: The later function replaces the earlier one and keeps its place in the order."],
        ]);
        expect(finding("aetswg-011-0009")).toEqual([
            [8, 0, "State 'State_Bad' is registered with '5', which is not a function: The state machine logs Invalid State and stops when it reaches this state."],
        ]);
        expect(finding("aetswg-011-0013")).toEqual([
            [14, 1, "'message' compared with '\"OnUpdate\"': The state machine passes only OnEnter, OnUpdate or OnExit."],
        ]);
        expect(finding("aetswg-011-0008")).toEqual([
            [15, 0, "State 'State_Nowhere' is not registered: Set_Next_State ignores it and the state machine stays where it is."],
        ]);
        expect(finding("aetswg-011-0012")).toEqual([
            [19, 1, "State handler 'State_Wait' takes 2 parameters: The state machine passes one, the message - OnEnter, OnUpdate or OnExit."],
        ]);
        expect(all.filter((d) => /^aetswg-011-001[4-7]$/.test(d.code))).toEqual([]);
    });

    it("offers the registered states inside Set_Next_State's quotes", async () => {
        await replaceLine(file, 15, '        Set_Next_State("")');
        const offered = await completions(file, { line: 15, text: '("")', offset: 2 });
        const states = offered.filter((c) => /^State_/.test(c.label));
        expect(states).toEqual(
            expect.arrayContaining([
                { label: "State_Init", detail: "state - handler State_Init" },
                { label: "State_Wait", detail: "state - handler State_Wait" },
                { label: "State_Done", detail: "state - handler State_Done" },
                { label: "State_Bad", detail: "state" },
            ]),
        );
        expect(states.filter((c) => c.label === "State_Done").length).toBe(1);
        await revert(file);
    });

    after(() => removeScripts(file));
});
