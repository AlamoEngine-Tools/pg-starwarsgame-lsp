// SET-06, before the reload: aet-eaw-edit.lsp.enabled is no longer a setting.
import { expect } from "@wdio/globals";
import { editUserSettingsJson, serverReady, unknownSettings } from "../protocol.js";

describe("SET-06", () => {
    it("marks aet-eaw-edit.lsp.enabled unknown", async () => {
        await serverReady();
        editUserSettingsJson({ "aet-eaw-edit.lsp.enabled": false });
        expect(unknownSettings()).toContain("aet-eaw-edit.lsp.enabled");
    });
});
