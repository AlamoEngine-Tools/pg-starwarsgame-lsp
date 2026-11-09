// XML-31 (#194): Disregard_Space_Layer_For_Guard_Attack_Move is a tag the 64-bit game reads on a game object,
// so the schema declares it and the XML editor raises no unknown-tag finding on it. A tag the game does not
// read, in the same object, still gets one - which is what proves the file was checked at all.
import { expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { diagnosticsWhen } from "./xmlEditor.js";

const file = "C:\\temp\\aet-test-XML-guard\\Data\\XML\\GuardUnits.xml";
const UNKNOWN_TAG = "aetswg-006-0012";
const KNOWN_LINE = 4;
const UNKNOWN_LINE = 5;

describe("XML-31", () => {
    before(async () => {
        await serverReady();
    });

    it("reports the tag the game does not read", async () => {
        const all = await diagnosticsWhen(file, (d) => d.some((x) => x.code === UNKNOWN_TAG && x.line === UNKNOWN_LINE));
        expect(all.filter((x) => x.line === UNKNOWN_LINE).map((x) => x.code)).toContain(UNKNOWN_TAG);
    });

    it("reports nothing on Disregard_Space_Layer_For_Guard_Attack_Move", async () => {
        const all = await diagnosticsWhen(file, (d) => d.some((x) => x.line === UNKNOWN_LINE));
        expect(all.filter((x) => x.line === KNOWN_LINE)).toEqual([]);
    });
});
