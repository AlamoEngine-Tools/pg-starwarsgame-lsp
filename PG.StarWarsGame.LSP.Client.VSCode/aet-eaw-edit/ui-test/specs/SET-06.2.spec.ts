// SET-06, after the reload with aet-eaw-edit.lsp.enabled false: the server starts anyway.
import { serverReady } from "../protocol.js";

describe("SET-06 after the reload", () => {
    it("starts the server whatever the old key says", async () => {
        await serverReady();
    });
});
