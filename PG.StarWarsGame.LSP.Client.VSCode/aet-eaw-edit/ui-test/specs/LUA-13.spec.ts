// LUA-13: entry points are documented on hover.
import { expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { hoverWhen, plain, script } from "./luaEditor.js";

describe("LUA-13", () => {
    before(async () => {
        await serverReady();
    });

    it("documents Story_Handle_Esc, Story_Mode_Service and Definitions as entry points", async () => {
        const esc = plain(await hoverWhen(script("Story", "Story_empire_acti_m02_land.lua"), { line: 1142, text: "Story_Handle_Esc" }, (c) =>
            c.join("\n").includes("entry point"),
        ));
        expect(esc).toContain("function Story_Handle_Esc - entry point");
        expect(esc).toContain("Called by the game when the player presses Escape in story mode.");
        expect(esc).toContain("Called by the game - scripts: story");

        const service = plain(await hoverWhen(script("Story", "Story_empire_acti_m03_space.lua"), { line: 1675, text: "Story_Mode_Service" }, (c) =>
            c.join("\n").includes("entry point"),
        ));
        expect(service).toContain("function Story_Mode_Service - entry point");
        expect(service).toContain(
            "Called on every pump by the command library's Pump_Service, if defined, whatever the state of the story events.",
        );
        expect(service).toContain("Called by PGCommands, if defined - scripts: object, story");

        const definitions = plain(await hoverWhen(script("Gameobject", "Interdictor.lua"), { line: 44, text: "Definitions" }, (c) =>
            c.join("\n").includes("entry point"),
        ));
        expect(definitions).toContain("function Definitions - entry point");
        expect(definitions).not.toContain("Not documented on the site; see the library source.");
    });
});
