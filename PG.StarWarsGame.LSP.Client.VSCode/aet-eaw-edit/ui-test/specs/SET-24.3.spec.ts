// SET-24, ModinfoOnly: a modinfo.json alone is a mod; the project file takes modinfo's name.
import path from "node:path";
import { expect } from "@wdio/globals";
import { createProjectFile, notification, notifications, projectOffer } from "../protocol.js";

const mod = "C:\\temp\\aet-test-SET-24\\ModinfoOnly";

describe("SET-24 ModinfoOnly", () => {
    it("is offered a project file, and Create names it after modinfo.json and lists no directories", async () => {
        await notification((m) => m === projectOffer(mod));
        expect((await notifications()).filter((n) => n.message.startsWith("No .pgproj file was found"))).toEqual([]);

        const written = await createProjectFile(mod, path.join(mod, "Modinfo_Only.pgproj"));
        expect(written.name).toBe("Modinfo Only");
        expect(written.directories).toEqual({});
    });
});
