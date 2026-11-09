// XML-33 (#292): the shipped particle systems name their sprite sheets by the art machine's absolute path
// (C:\Projects\Starwars\Art\Textures\Game\p_particle_master.tga and two more, in 37 files per game). The game
// opens the part after the last backslash in its texture directories, and they render; the XML check agreed
// with the game only after it did the same, and reported every such particle's texture missing before. The
// file is left as it was: the control line goes into the editor buffer and the buffer is reverted.
import { browser, expect } from "@wdio/globals";
import path from "node:path";
import { foc, insertLineAfter, serverReady } from "../protocol.js";
import { diagnosticsWhen } from "./xmlEditor.js";

const particles = path.join(foc, "Data", "XML", "Particles.xml");
const MISSING_TEXTURE = "aetswg-004-0012";
const UNKNOWN_TAG = "aetswg-006-0012";
// p_lava_bubbles.alo, one of the 37, named for land and for space.
const LAVA_BUBBLES_LINES = [71, 72];
const CONTROL_AFTER = 72;

describe("XML-33", () => {
    before(async () => {
        await serverReady();
    });

    it("reports no missing texture for a particle naming its sprite sheet by an absolute path", async () => {
        await insertLineAfter(particles, CONTROL_AFTER - 1, "<Zzq_Not_A_Tag>1</Zzq_Not_A_Tag>");
        const all = await diagnosticsWhen(particles, (d) => d.some((x) => x.code === UNKNOWN_TAG && x.line === CONTROL_AFTER + 1));
        expect(all.filter((x) => x.code === MISSING_TEXTURE && LAVA_BUBBLES_LINES.includes(x.line))).toEqual([]);
    });

    after(async () => {
        await browser.executeWorkbench(async (vscode) => {
            await vscode.commands.executeCommand("workbench.action.files.revert");
            await vscode.commands.executeCommand("workbench.action.closeActiveEditor");
        });
    });
});
