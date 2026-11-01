// XML-23: a dropped vanilla file - Fix all repairs every mismatched end tag in one edit, then Format Document runs.
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { clearNotifications, eaw, notifications, revertAndClose, serverReady } from "../protocol.js";
import { applyQuickFix, diagnostics, diagnosticsWhen, formatDocument, lineText } from "./xmlEditor.js";

const file = path.join(eaw, "Data", "XML", "Guidialogs.xml");
const LINES = [3181, 3334, 3343, 3435, 3446, 3457, 3468, 3509, 4460];
const codes = (all: { code: string }[]): string[] => all.map((d) => d.code).filter((c) => !c.startsWith("aetswg-012-")).sort();

describe("XML-23", () => {
    before(async () => {
        await serverReady();
        await clearNotifications();
    });

    it("fixes every end tag, then formats", async () => {
        const before = await diagnosticsWhen(file, (d) => d.some((x) => x.code === "aetswg-012-3002"));
        await applyQuickFix(file, 3181, "</Bottom_Color>", "Fix all repairable XML structure problems in this file");
        for (const line of LINES) expect(await lineText(file, line)).toContain("</Top_Color>");

        const after = await diagnosticsWhen(file, (d) => !d.some((x) => x.code.startsWith("aetswg-012-")), 60_000);
        expect(codes(after)).toEqual(codes(before));

        await formatDocument(file);
        await browser.pause(5_000);
        expect((await notifications()).filter((n) => n.message.startsWith("Formatting refused"))).toEqual([]);
        expect(codes(await diagnostics(file))).toEqual(codes(before));
    });

    after(async () => {
        await revertAndClose(file);
    });
});
