// SET-02: one VSIX for win32-x64 and no separate server archive; the extension runs its own server.
import fs from "node:fs";
import path from "node:path";
import { expect } from "@wdio/globals";
import { client, repo } from "../paths.js";
import { outputChannels, serverReady } from "../protocol.js";

describe("SET-02", () => {
    const release = fs.readFileSync(path.join(repo, ".github", "workflows", "release.yml"), "utf8");

    it("packages one VSIX for win32-x64", () => {
        expect(release).toMatch(/'package', '--no-dependencies', '--target', 'win32-x64'/);
    });

    it("uploads no server archive, only its SBOM", () => {
        expect(release).not.toMatch(/\.zip/);
        expect(release).toContain("PG.StarWarsGame.LSP.Server-$VERSION-win-x64.sbom.spdx.json");
    });

    it("runs the server from the extension's own folder", async () => {
        await serverReady();
        expect((outputChannels().get("EaWEdit") ?? "").toLowerCase()).toContain(
            `bundled: ${path.join(client, "server", "PG.StarWarsGame.LSP.Server.exe")}`.toLowerCase(),
        );
    });
});
