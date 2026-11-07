// SET-22, before the reload: Not now leaves no project file and no error this session; a restart of
// the server brings the missing-project error instead of a second offer.
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

const mod = "C:\\temp\\aet-test-SET-22\\ModB";

describe("SET-22", () => {
    it("leaves no project file and no error after Not now", async () => {
        await openFile(path.join(mod, "Data", "XML", "TestUnits.xml"));
        await notification((m) => m === projectOffer(mod));
        await takeNotificationAction(projectOffer(mod), "Not now");
        await browser.pause(10_000);

        expect(fs.readdirSync(mod).filter((f) => f.endsWith(".pgproj"))).toEqual([]);
        expect((await notifications()).filter((n) => n.message.startsWith("No .pgproj file was found"))).toEqual([]);
    });

    it("shows the missing-project error, not the offer, once the server restarts", async () => {
        await clearNotifications();
        await command("aet-eaw-edit.lsp.restartServer");
        const error = await notification((m) => m.startsWith(noProjectError(mod)));
        expect(error.type).toBe("error");
        expect((await notifications()).filter((n) => n.message === projectOffer(mod))).toEqual([]);
    });
});
