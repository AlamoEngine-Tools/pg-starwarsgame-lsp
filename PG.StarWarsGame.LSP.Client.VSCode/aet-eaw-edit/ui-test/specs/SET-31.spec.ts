// SET-31 (#288): a window with an Empire at War project and a Forces of Corruption project, and the one local
// baseline setting naming the FoC file. Each project loads the baseline of its own game: a FoC-only object is
// game data in the FoC project and absent from the EaW project, which used to list it as the game's.
import { expect } from "@wdio/globals";
import { projectsReady } from "../protocol.js";
import { openOutline, rows, search, tableReady } from "./modBrowser.js";
import { closeAll, inNewestWebview } from "./storyGraph.js";

const FOC_ONLY = "Executor_Super_Star_Destroyer";

/** Whether the project's Game Data table lists the object, after a search for it; the Mod Browser's rows carry the folder names. */
async function lists(project: string): Promise<boolean> {
    await closeAll();
    await openOutline("Game Data", project);
    return inNewestWebview(async () => {
        await tableReady();
        await search(FOC_ONLY);
        return (await rows()).some((r) => r.cells.includes(FOC_ONLY));
    });
}

describe("SET-31", () => {
    before(async () => {
        await projectsReady(2);
    });

    it("lists the FoC-only object in the FoC project", async () => {
        expect(await lists("foc-mod")).toBe(true);
    });

    it("does not list it in the EaW project", async () => {
        expect(await lists("eaw-mod")).toBe(false);
    });
});
