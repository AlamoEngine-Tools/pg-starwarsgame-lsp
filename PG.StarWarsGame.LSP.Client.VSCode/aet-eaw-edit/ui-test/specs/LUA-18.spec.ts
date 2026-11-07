// LUA-18: story mode in vanilla - an undefined story event handler (Engine10), a handler without a parameter, and
// story event names not renamed.
import { expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { diagnosticsWhen } from "./xmlEditor.js";
import { hoverWhen, prepareRename, script } from "./luaEditor.js";

const m03 = script("Story", "Story_empire_acti_m03_space.lua");
const m06 = script("Story", "Story_empire_actii_m06_land.lua");
const plain = (cards: string[]): string => cards.join("\n").replace(/[*`]/g, "");

describe("LUA-18", () => {
    before(async () => {
        await serverReady();
    });

    it("warns of the undefined handler and explains the story event", async () => {
        const all = await diagnosticsWhen(m03, (d) => d.some((x) => x.code === "aetswg-011-0010" && x.line === 56));
        expect(all.find((d) => d.code === "aetswg-011-0010" && d.line === 56)).toMatchObject({
            severity: 1,
            message:
                "Story event 'Empire_M2_Pirate_Base_is_Dead' names 'State_M2_Pirate_Base_is_Dead', which is not defined: The entry is nil, so the event is never registered and does nothing.",
        });
        const card = plain(await hoverWhen(m03, { line: 56, text: "Empire_M2_Pirate_Base_is_Dead" }, (c) => c.length > 0));
        expect(card).toContain("state Empire_M2_Pirate_Base_is_Dead - handler State_M2_Pirate_Base_is_Dead");
        expect(card).toContain("A story event: Entered when the event fires and the machine is not already changing state");
        expect(await prepareRename(m03, { line: 56, text: "Empire_M2_Pirate_Base_is_Dead" })).toMatch(/can't be renamed|cannot be renamed/i);
    });

    it("warns of a handler taking no parameter", async () => {
        const all = await diagnosticsWhen(m06, (d) => d.some((x) => x.code === "aetswg-011-0012" && x.line === 337));
        expect(all.find((d) => d.code === "aetswg-011-0012" && d.line === 337)).toMatchObject({
            severity: 1,
            message:
                "State handler 'State_Empire_A02M06_Trigger_Rebels' takes 0 parameters: The state machine passes one, the message - OnEnter, OnUpdate or OnExit.",
        });
    });
});
