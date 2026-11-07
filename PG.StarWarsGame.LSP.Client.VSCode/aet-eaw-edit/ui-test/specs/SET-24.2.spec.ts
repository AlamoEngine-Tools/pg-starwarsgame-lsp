// SET-24, MegOnly: a Data folder with an archive and nothing else is a mod; its project file lists no
// directories and is named after the folder.
import path from "node:path";
import { expect } from "@wdio/globals";
import { createProjectFile, notification, notifications, projectOffer } from "../protocol.js";

const mod = "C:\\temp\\aet-test-SET-24\\MegOnly";

describe("SET-24 MegOnly", () => {
    it("is offered a project file as soon as it is opened, and Create lists no directories", async () => {
        await notification((m) => m === projectOffer(mod));
        expect((await notifications()).filter((n) => n.message.startsWith("No .pgproj file was found"))).toEqual([]);

        const written = await createProjectFile(mod, path.join(mod, "MegOnly.pgproj"));
        expect(written.directories).toEqual({});
    });
});
