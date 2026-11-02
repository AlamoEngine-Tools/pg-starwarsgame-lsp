// LUA-03: the features delegated to the analyzer - completion, signature help, folding, semantic tokens, inlay
// hints and the usages lens.
import { expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { codeLenses } from "./xmlEditor.js";
import {
    completions,
    foldingRanges,
    hoverWhen,
    inlayHints,
    insertLine,
    references,
    revert,
    script,
    semanticTokenAt,
    signatureHelp,
} from "./luaEditor.js";

const interdictor = script("Gameobject", "Interdictor.lua");

describe("LUA-03", () => {
    before(async () => {
        await serverReady();
        await hoverWhen(interdictor, { line: 112, text: "FindDeadlyEnemy" }, (c) => c.join("\n").includes("---"));
    });

    it("completes, and helps with a signature", async () => {
        const line = await insertLine(interdictor, 108, "\tUnder_");
        expect((await completions(interdictor, { line, text: "Under_", offset: 6 })).map((c) => c.label)).toContain(
            "Under_Missile_Attack",
        );
        await revert(interdictor);

        await insertLine(interdictor, 108, "\tObject.");
        const members = (await completions(interdictor, { line, text: "Object.", offset: 7 })).map((c) => c.label);
        expect(members).toEqual(expect.arrayContaining(["Get_Owner", "Activate_Ability"]));
        await revert(interdictor);

        await insertLine(interdictor, 108, "\tFind_Player(");
        const help = await signatureHelp(interdictor, { line, text: "Find_Player(", offset: 12 });
        expect(help?.label).toMatch(/faction_name: \(?string\|"local"\)?/);
        // The reported failure: the completion there offered every local and global of the file. Before the
        // quotes it offers the values quoted - "local" and the factions - and nothing else.
        const offered = (await completions(interdictor, { line, text: "Find_Player(", offset: 12 })).map((c) => c.label);
        expect(offered).toEqual(expect.arrayContaining(['"local"', '"Empire"', '"Rebel"']));
        expect(offered.filter((l) => !l.startsWith('"'))).toEqual([]);
        await revert(interdictor);
    });

    it("folds State_Init, types its tokens and hints at arguments", async () => {
        expect(await foldingRanges(interdictor)).toEqual(expect.arrayContaining([{ start: 51, end: 103 }]));
        expect(await semanticTokenAt(interdictor, { line: 94, text: "Under_Missile_Attack" })).toBe("function");
        const hints = await inlayHints(interdictor, 1, 140);
        expect(hints.length).toBeGreaterThan(0);
    });

    // A function's lens does not count its own declaration (f9b28d72); the references list holds it.
    it("counts Cancel_Interdiction's one usage", async () => {
        const lens = (await codeLenses(interdictor)).find((l) => l.line === 106);
        expect(lens?.title).toBe("1 usage");
        const places = await references(interdictor, { line: 106, text: "Cancel_Interdiction" });
        expect(places.map((p) => p.replace(/^.*[\\/]/, "")).sort()).toEqual(["Interdictor.lua:106", "Interdictor.lua:90"]);
    });
});
