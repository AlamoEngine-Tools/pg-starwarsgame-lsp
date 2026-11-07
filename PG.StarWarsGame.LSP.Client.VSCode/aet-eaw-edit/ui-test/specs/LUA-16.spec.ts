// LUA-16: the state machine - hovers, going to a handler from its state name, renaming a state name.
import { expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { definition, hoverWhen, rename, script } from "./luaEditor.js";

const interdictor = script("Gameobject", "Interdictor.lua");
const plain = (cards: string[]): string => cards.join("\n").replace(/[*`]/g, "");

describe("LUA-16", () => {
    before(async () => {
        await serverReady();
    });

    it("explains the state name, its handler and the message", async () => {
        expect(plain(await hoverWhen(interdictor, { line: 47, text: '"State_Init"', offset: 2 }, (c) => c.length > 0))).toContain(
            "initial state State_Init - handler State_Init",
        );
        const handler = plain(await hoverWhen(interdictor, { line: 47, text: "State_Init", nth: 2 }, (c) => c.length > 0));
        expect(handler).toContain("state handler - initial state State_Init");
        expect(handler).toContain("Called with OnEnter once, OnUpdate every service tick until the state changes, then OnExit once");
        const message = plain(await hoverWhen(interdictor, { line: 51, text: "message" }, (c) => c.join("\n").includes("---")));
        expect(message).toContain("message - state State_Init");
        expect(message).toContain(
            "OnEnter (1) once on entering the state, OnUpdate (2) every service tick while it stays, OnExit (3) once on leaving it",
        );
        expect(message).toMatch(/1\s*\|\s*2\s*\|\s*3/);
    });

    it("goes from the state name to its handler, and renames the name alone", async () => {
        const places = await definition(interdictor, { line: 47, text: '"State_Init"', offset: 2 });
        expect(places.map((p) => p.replace(/^.*[\\/]/, ""))).toEqual(["Interdictor.lua:51"]);
        const edits = (await rename(interdictor, { line: 47, text: '"State_Init"', offset: 2 }, "State_Start")).edits;
        expect(edits.map((p) => p.replace(/^.*[\\/]/, ""))).toEqual(["Interdictor.lua:47"]);
    });

    // The reported failure: renaming the handler left Define_State's argument behind, and the argument could not
    // be renamed at all. Either one now renames both, and the quoted state name stays as it is.
    it("renames a handler and its Define_State argument together, from either one", async () => {
        const fromFunction = (await rename(interdictor, { line: 51, text: "State_Init" }, "State_Begin")).edits;
        expect(fromFunction.map((p) => p.replace(/^.*[\\/]/, "")).sort()).toEqual(["Interdictor.lua:47", "Interdictor.lua:51"]);
        const fromArgument = (await rename(interdictor, { line: 47, text: "State_Init", nth: 2 }, "State_Begin")).edits;
        expect(fromArgument.map((p) => p.replace(/^.*[\\/]/, "")).sort()).toEqual(["Interdictor.lua:47", "Interdictor.lua:51"]);
    });
});
