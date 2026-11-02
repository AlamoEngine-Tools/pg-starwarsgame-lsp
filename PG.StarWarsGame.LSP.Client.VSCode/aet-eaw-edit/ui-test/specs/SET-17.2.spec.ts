// SET-17, second part - the retest of the reported failure: a project whose translations are an XML file in the
// format the EaW text editor writes (no namespace, a capital Key) loads its texts, so a key shows its text - in
// XML as the inlay hint after it.
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { inlayHints } from "./luaEditor.js";

const units = path.join("C:\\temp\\aet-set17-xml", "data", "xml", "AetSet17Units.xml");

describe("SET-17", () => {
    before(async () => {
        await serverReady();
    });

    it("shows the text of a key from an XML translation file", async () => {
        let hints: { line: number; label: string }[] = [];
        await browser.waitUntil(async () => (hints = await inlayHints(units, 1, 6)).some((h) => h.label.includes("Test unit")), {
            timeout: 90_000,
            timeoutMsg: "No inlay hint carries the key's text",
        });
        expect(hints.find((h) => h.line === 4)?.label).toContain("Test unit from XML");
    });
});
