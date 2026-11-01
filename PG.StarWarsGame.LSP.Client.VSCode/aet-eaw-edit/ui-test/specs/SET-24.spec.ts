// SET-24, ScriptsOnly: a mod with scripts and no Data/XML is offered a project file; Create writes it
// with the scripts and no xml, named after the folder.
import path from "node:path";
import { expect } from "@wdio/globals";
import { createProjectFile, notification, notifications, openFile, projectOffer } from "../protocol.js";

const mod = "C:\\temp\\aet-test-SET-24\\ScriptsOnly";

describe("SET-24 ScriptsOnly", () => {
    it("is offered a project file and Create writes the scripts and no xml", async () => {
        await openFile(path.join(mod, "Data", "Scripts", "test.lua"));
        await notification((m) => m === projectOffer(mod));
        expect((await notifications()).filter((n) => n.message.startsWith("No .pgproj file was found"))).toEqual([]);

        const written = await createProjectFile(mod, path.join(mod, "ScriptsOnly.pgproj"));
        expect(written.directories).toEqual({ scripts: ["Data/Scripts"] });
    });
});
