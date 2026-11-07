// XML-22: the vanilla EaW files the game drops, their fixes, Format Document refused, and tolerated text.
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { clearNotifications, eaw, notification, serverReady } from "../protocol.js";
import { closeAll } from "./storyGraph.js";
import { diagnostics, diagnosticsWhen, formatDocument, quickFixes } from "./xmlEditor.js";

const xml = (name: string): string => path.join(eaw, "Data", "XML", name);

describe("XML-22", () => {
    before(async () => {
        await serverReady();
        await clearNotifications();
    });

    it("reports Guidialogs.xml's mismatched end tag once, with its fix, and refuses to format it", async () => {
        const all = await diagnosticsWhen(xml("Guidialogs.xml"), (d) => d.some((x) => x.code.startsWith("aetswg-012-")));
        const structure = all.filter((d) => d.code.startsWith("aetswg-012-"));
        expect(structure.map((d) => [d.code, d.line, d.severity])).toEqual([["aetswg-012-3002", 3181, 0]]);
        expect(structure[0].message).toBe(
            "End tag </Bottom_Color> does not match <Top_Color>: The game compares them byte for byte and drops the whole file",
        );
        const fixes = await quickFixes(xml("Guidialogs.xml"), 3181, "</Bottom_Color>");
        expect(fixes.find((f) => f.preferred)?.title).toBe("Rename the end tag to </Top_Color>");

        await formatDocument(xml("Guidialogs.xml"));
        const refused = await notification((m) => m.startsWith("Formatting refused"));
        expect(refused).toMatchObject({ type: "warning", message: "Formatting refused: Not well-formed XML at line 3181, column 31" });
    });

    it("reports Surfacefx.xml's end tag with its fix", async () => {
        const all = await diagnosticsWhen(xml("Surfacefx.xml"), (d) => d.some((x) => x.code.startsWith("aetswg-012-")));
        const structure = all.filter((d) => d.code.startsWith("aetswg-012-"));
        expect(structure.map((d) => [d.code, d.line, d.severity])).toEqual([["aetswg-012-3003", 227, 0]]);
        expect(structure[0].message).toBe(
            "End tag </SoundFX_name> does not match <SoundFX_Name>: The game compares them byte for byte and drops the whole file",
        );
        const fixes = await quickFixes(xml("Surfacefx.xml"), 227, "</SoundFX_name>");
        expect(fixes.map((f) => f.title)).toContain("Rename the end tag to </SoundFX_Name>");
    });

    it("warns of the text after a child element in act IV", async () => {
        const all = await diagnosticsWhen(xml("Story_campaign_empire_act_iv.xml"), (d) =>
            d.some((x) => x.code === "aetswg-015-0004"),
        );
        const warning = all.find((d) => d.code === "aetswg-015-0004" && d.line === 453);
        expect(warning).toMatchObject({
            severity: 1,
            message: "Text after a child element: The game skips it; values must come before the first child",
        });
    });

    it("tolerates the comment inside a value in GameConstants.xml", async () => {
        // An absence: read after the server has had time to publish the file's findings.
        await diagnostics(xml("Gameconstants.xml"));
        await browser.pause(15_000);
        const all = await diagnostics(xml("Gameconstants.xml"));
        expect(all.filter((d) => d.code === "aetswg-015-0006" && d.line === 915)).toEqual([]);
        await closeAll();
    });
});
