// SET-22, after the reload: Not now lasted for the session only - the offer comes again, no error.
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { notification, notifications, openFile, projectOffer } from "../protocol.js";

const mod = "C:\\temp\\aet-test-SET-22\\ModB";

describe("SET-22 after the reload", () => {
    it("offers the project file again, with no error", async () => {
        await openFile(path.join(mod, "Data", "XML", "TestUnits.xml"));
        await notification((m) => m === projectOffer(mod));
        await browser.pause(5_000);
        expect((await notifications()).filter((n) => n.message.startsWith("No .pgproj file was found"))).toEqual([]);
    });
});
