// LUA-01: the Lua analyzer starts with the server and answers in a script - lenses, the merged hover, symbols.
import { expect } from "@wdio/globals";
import { logChannels, logLine, serverReady } from "../protocol.js";
import { codeLenses } from "./xmlEditor.js";
import { documentSymbols, hoverWhen, plain, script } from "./luaEditor.js";

const SERVER = "Alamo Engine Tools - Empire at War Edit";
const interdictor = script("Gameobject", "Interdictor.lua");
const USAGES = /^\d+ usages?$/;

describe("LUA-01", () => {
    before(async () => {
        await serverReady();
    });

    it("starts the analyzer for the project's scripts and merges its hover under this server's", async () => {
        const cards = await hoverWhen(interdictor, { line: 112, text: "FindDeadlyEnemy" }, (c) => c.join("\n").includes("---"));
        const started = await logLine(/Lua analyzer started \(pid \d+\) for /, SERVER);
        expect(started).toMatch(/for .*[\\/]eaw[\\/]Data[\\/]Scripts/i);
        expect([...logChannels().values()].join("\n")).not.toContain("Lua analyzer not started");

        expect(cards.length).toBe(1);
        const [rawOurs, analyzer] = cards[0].split(/\n-{3,}\n/);
        expect(rawOurs.split("\n")[0]).toMatch(/^\*\*function\*\*/);
        const ours = plain([rawOurs]);
        expect(ours).toContain("function FindDeadlyEnemy(task_force, value?, player?)");
        expect(ours).toContain("Finds a deadly enemy for a task force.");
        expect(ours).toContain("Parameters:");
        expect(ours).toMatch(/task_force.*TaskForce\|AITargetLocation\|GameObject/);
        expect(ours).toMatch(/Returns.*GameObject/);
        expect(analyzer?.trim().length ?? 0).toBeGreaterThan(0);
    });

    it("puts a usages lens above every function and lists them as symbols", async () => {
        const lenses = (await codeLenses(interdictor)).filter((l) => USAGES.test(l.title));
        expect(lenses.map((l) => l.line)).toContain(44);
        const symbols = await documentSymbols(interdictor);
        const functions = ["Definitions", "State_Init", "Cancel_Interdiction", "Under_Missile_Attack", "Cancel_Missile_Shield"];
        expect(symbols).toEqual(expect.arrayContaining(functions));
        expect(lenses.length).toBeGreaterThanOrEqual(functions.length);
    });
});
