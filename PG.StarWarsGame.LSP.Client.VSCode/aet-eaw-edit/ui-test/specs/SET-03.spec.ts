// SET-03: ten settings on the settings page, fourteen development settings behind @tag:advanced.
import fs from "node:fs";
import path from "node:path";
import { expect } from "@wdio/globals";
import { client, readJson } from "../paths.js";
import { serverReady, settingsFound, settingsShown } from "../protocol.js";

const ext = "@ext:AlamoEngineTools.aet-eaw-edit";

describe("SET-03", () => {
    before(async () => {
        await serverReady();
    });

    it("shows ten settings and no feature flag on the plain search", async () => {
        expect(await settingsFound(ext)).toBe(10);
        const shown = await settingsShown();
        expect(shown.filter((s) => /Features/i.test(s.category))).toEqual([]);
    });

    it("shows fourteen behind @tag:advanced", async () => {
        expect(await settingsFound(`${ext} @tag:advanced`)).toBe(14);
    });

    it("says where an unset game directory is found, and leaves no placeholder in the manifest", () => {
        const nls: Record<string, string> = readJson(path.join(client, "package.nls.json"));
        for (const key of ["baseGameDirectory", "expansionDirectory"])
            expect(nls[`config.lsp.source.${key}.markdownDescription`].trimEnd()).toMatch(
                /Empty, the install is found in the Steam library or the Windows registry\.$/,
            );
        const manifest = fs.readFileSync(path.join(client, "package.json"), "utf8");
        for (const key of manifest.match(/%[\w.]+%/g) ?? []) expect(nls[key.slice(1, -1)]).toBeDefined();
    });
});
