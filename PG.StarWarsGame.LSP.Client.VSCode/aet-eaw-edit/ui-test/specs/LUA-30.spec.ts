// LUA-30: the removed Lua feature flags are unknown and switch nothing off. The setting is written before the
// window opens (the part's sidecar), which is what a reload after writing it gives.
import { expect } from "@wdio/globals";
import { serverReady, unknownSettings } from "../protocol.js";
import { codeLenses } from "./xmlEditor.js";
import { hoverWhen, inlayHints, script } from "./luaEditor.js";

const KEYS = ["aet-eaw-edit.features.lua.codeLens", "aet-eaw-edit.features.lua.inlayHints", "aet-eaw-edit.features.lua.analyzer"];
const interdictor = script("Gameobject", "Interdictor.lua");

describe("LUA-30", () => {
    before(async () => {
        await serverReady();
    });

    it("marks the three keys unknown", () => {
        expect(unknownSettings()).toEqual(expect.arrayContaining(KEYS));
    });

    it("still shows the lenses and the hints", async () => {
        await hoverWhen(interdictor, { line: 112, text: "FindDeadlyEnemy" }, (c) => c.join("\n").includes("---"));
        expect((await codeLenses(interdictor)).some((l) => /^\d+ usages?$/.test(l.title))).toBe(true);
        expect((await inlayHints(interdictor, 1, 140)).length).toBeGreaterThan(0);
    });
});
