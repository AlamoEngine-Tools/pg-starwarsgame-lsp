// SET-07: lsp.locale, modVerify.* and shaders.sourceUrl are gone; the server is English and silent
// about ModVerify.
import path from "node:path";
import { expect } from "@wdio/globals";
import {
    eaw,
    editUserSettingsJson,
    extensionLog,
    findings,
    hoverWhenReady,
    outputChannels,
    restartServer,
    serverReady,
    unknownSettings,
} from "../protocol.js";

const removed = {
    "aet-eaw-edit.lsp.locale": "de",
    "aet-eaw-edit.modVerify.enabled": true,
    "aet-eaw-edit.modVerify.executable": "C:\\nothing.exe",
    "aet-eaw-edit.shaders.sourceUrl": "http://127.0.0.1:9/x.zip",
};

describe("SET-07", () => {
    before(async () => {
        await serverReady();
        editUserSettingsJson(removed);
    });

    it("marks all four keys unknown", async () => {
        const unknown = unknownSettings();
        for (const key of Object.keys(removed)) expect(unknown).toContain(key);
    });

    it("starts in English and says nothing of ModVerify", async () => {
        await restartServer();
        const corvettes = path.join(eaw, "Data", "XML", "Spaceunitscorvettes.xml");
        expect(await hoverWhenReady(corvettes, 161, 4)).toContain("Population_Value");
        for (const f of await findings(corvettes)) expect(f.message).toMatch(/^[\x20-\x7E]*$/);
        expect(extensionLog() + (outputChannels().get("EaWEdit") ?? "")).not.toMatch(/ModVerify/i);
    });

    after(() => {
        editUserSettingsJson(Object.fromEntries(Object.keys(removed).map((k) => [k, null])));
    });
});
