// SET-27: a window at a mod's Data folder starts a server, offers no project file (it belongs beside
// Data, outside the window) and shows the missing-project error.
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { lspStatus, noProjectError, notification, notifications, openFile } from "../protocol.js";

const data = "C:\\temp\\aet-test-SET-27\\ModD\\Data";

describe("SET-27", () => {
    it("starts a server and shows the missing-project error, with no offer", async () => {
        await openFile(path.join(data, "XML", "test.xml"));
        const error = await notification((m) => m.startsWith(noProjectError(data)));
        expect(error.type).toBe("error");
        await browser.waitUntil(async () => (await lspStatus()) !== null, { timeout: 30_000 });
        expect((await notifications()).filter((n) => n.message.startsWith("EaWEdit: Mod without a project file"))).toEqual([]);
    });
});
