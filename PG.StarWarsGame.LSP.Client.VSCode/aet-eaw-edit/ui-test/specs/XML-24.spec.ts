// XML-24: the vanilla registries are read by the game, and their entries are not checked as unrelated tags.
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { eaw, serverReady } from "../protocol.js";
import { diagnostics } from "./xmlEditor.js";

const REGISTRIES = ["Campaignfiles.xml", "Gameobjectfiles.xml", "Sfxeventfiles.xml", "Factionfiles.xml", "Hardpointdatafiles.xml"];

describe("XML-24", () => {
    before(async () => {
        await serverReady();
    });

    it("reports none of the five registries as read by nothing", async () => {
        const files = REGISTRIES.map((name) => path.join(eaw, "Data", "XML", name));
        for (const file of files) await diagnostics(file);
        // Absences: read after the server has had time to publish each file's findings.
        await browser.pause(15_000);
        for (const file of files) {
            const all = await diagnostics(file);
            expect({ file: path.basename(file), findings: all.map((d) => `${d.code}@${d.line}: ${d.message}`) }).toEqual({
                file: path.basename(file),
                findings: [],
            });
        }
    });
});
