// SET-23: Not for this folder writes nothing and shows no error at first; afterwards the folder is
// never offered again and gets the missing-project error. The protocol sees "afterwards" after a
// reload; globalState does not survive one here (see run.mjs), so a server restart - which plans the
// window anew from the same list - stands in for it.
import fs from "node:fs";
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import {
    clearNotifications,
    command,
    noProjectError,
    notification,
    notifications,
    openFile,
    projectOffer,
    takeNotificationAction,
} from "../protocol.js";

const mod = "C:\\temp\\aet-test-SET-23\\ModC";

describe("SET-23", () => {
    it("writes nothing and shows no error after Not for this folder", async () => {
        await openFile(path.join(mod, "Data", "XML", "TestUnits.xml"));
        await notification((m) => m === projectOffer(mod));
        await takeNotificationAction(projectOffer(mod), "Not for this folder");
        await browser.pause(5_000);

        expect(fs.readdirSync(mod).filter((f) => f.endsWith(".pgproj"))).toEqual([]);
        expect((await notifications()).filter((n) => n.message.startsWith("No .pgproj file was found"))).toEqual([]);
    });

    it("is not offered again and shows the missing-project error", async () => {
        await clearNotifications();
        await command("aet-eaw-edit.lsp.restartServer");
        const error = await notification((m) => m.startsWith(noProjectError(mod)));
        expect(error.type).toBe("error");
        await browser.pause(3_000);
        expect((await notifications()).filter((n) => n.message === projectOffer(mod))).toEqual([]);
    });
});
