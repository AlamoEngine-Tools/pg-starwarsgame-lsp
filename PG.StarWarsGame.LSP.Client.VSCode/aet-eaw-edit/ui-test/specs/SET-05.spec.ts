// SET-05: removed feature flags show as unknown and switch nothing off; energyPool is still known.
import path from "node:path";
import { expect } from "@wdio/globals";
import { eaw, editUserSettingsJson, hoverWhenReady, restartServer, serverReady, unknownSettings } from "../protocol.js";
import { storyModes } from "./storyGraph.js";

describe("SET-05", () => {
    before(async () => {
        await serverReady();
        editUserSettingsJson({
            "aet-eaw-edit.features.tools.storyEditing": true,
            "aet-eaw-edit.features.lua.hover": false,
            "aet-eaw-edit.features.preview.energyPool": true,
        });
    });

    it("marks the two removed flags unknown and keeps energyPool", async () => {
        const unknown = unknownSettings();
        expect(unknown).toContain("aet-eaw-edit.features.tools.storyEditing");
        expect(unknown).toContain("aet-eaw-edit.features.lua.hover");
        expect(unknown).not.toContain("aet-eaw-edit.features.preview.energyPool");
    });

    it("enables no editing under the old flag name", async () => {
        await restartServer();
        expect(await storyModes("Full_Story_Campaign_Empire", "Empire")).toEqual(["View"]);
    });

    it("still answers the Lua hover", async () => {
        const script = path.join(eaw, "Data", "Scripts", "Story", "Story_empire_acti_m02_land.lua");
        expect(await hoverWhenReady(script, 185, 18)).toContain("Find_First_Object");
    });

    after(() => {
        editUserSettingsJson({
            "aet-eaw-edit.features.tools.storyEditing": null,
            "aet-eaw-edit.features.lua.hover": null,
            "aet-eaw-edit.features.preview.energyPool": null,
        });
    });
});
