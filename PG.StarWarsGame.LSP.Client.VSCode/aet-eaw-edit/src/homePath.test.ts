// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { homeAsTilde } from "./homePath";

const HOME = "C:\\Users\\someone";

describe("homeAsTilde", () => {
    it("names the home directory ~ wherever a line carries it", () => {
        assert.equal(
            homeAsTilde(
                "Starting LSP server: Bundled: C:\\Users\\someone\\.vscode\\extensions\\aet\\server\\Server.exe --log-dir=C:\\Users\\someone\\.aetswg\\logs\\3f9c --log-stem=My Mod",
                HOME,
                true,
            ),
            "Starting LSP server: Bundled: ~\\.vscode\\extensions\\aet\\server\\Server.exe --log-dir=~\\.aetswg\\logs\\3f9c --log-stem=My Mod",
        );
    });

    it("takes the path with either separator and, on Windows, in any case", () => {
        assert.equal(
            homeAsTilde(
                "  lsp.executable   = c:/users/SOMEONE/dev/Server.exe",
                HOME,
                true,
            ),
            "  lsp.executable   = ~/dev/Server.exe",
        );
    });

    it("keeps the case apart where the file system does", () => {
        assert.equal(
            homeAsTilde(
                "/home/Someone/mod and /home/someone/mod",
                "/home/someone",
                false,
            ),
            "/home/Someone/mod and ~/mod",
        );
    });

    it("takes the path inside a file URI, with the drive's colon encoded or not", () => {
        assert.equal(
            homeAsTilde(
                "file:///c%3A/Users/someone/mods/a.xml file:///C:/Users/someone/mods/b.xml",
                HOME,
                true,
            ),
            "file:///~/mods/a.xml file:///~/mods/b.xml",
        );
    });

    it("names the home directory itself", () => {
        assert.equal(
            homeAsTilde("home = C:\\Users\\someone", HOME, true),
            "home = ~",
        );
        assert.equal(homeAsTilde("'C:\\Users\\someone'", HOME, true), "'~'");
    });

    it("leaves another user's folder that only starts the same alone", () => {
        const line =
            "C:\\Users\\someone-else\\mod C:\\Users\\someone.old\\mod C:\\Users\\someone2";

        assert.equal(homeAsTilde(line, HOME, true), line);
    });

    it("leaves a line without the home directory as it is", () => {
        const line =
            "Projects: Alpha, Beta; folders without a project: C:\\temp\\gamma";

        assert.equal(homeAsTilde(line, HOME, true), line);
    });

    it("changes nothing for a home directory that is not known", () => {
        assert.equal(
            homeAsTilde("C:\\Users\\someone\\x", "", true),
            "C:\\Users\\someone\\x",
        );
    });
});
