// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { homeAsTilde, scrubbed } from "./homePath";

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

describe("homeAsTilde in escaped text", () => {
    it("takes a path whose backslashes are doubled, as JSON writes them", () => {
        assert.equal(
            homeAsTilde(
                '{"root":"C:\\\\Users\\\\someone\\\\AppData\\\\Local\\\\Temp\\\\x"}',
                HOME,
                true,
            ),
            '{"root":"~\\\\AppData\\\\Local\\\\Temp\\\\x"}',
        );
    });
});

/** A channel that writes the home directory as ~ whatever is written to it. */
describe("scrubbed", () => {
    function channel() {
        const written: unknown[][] = [];
        return {
            name: "C:\\Users\\someone\\named",
            written,
            get level(): number {
                return 3;
            },
            appendLine(value: string): void {
                written.push([value]);
            },
            error(error: string | Error, ...args: unknown[]): void {
                written.push([error, ...args]);
            },
            self(): unknown {
                return this;
            },
        };
    }
    const scrub = (text: string) => homeAsTilde(text, HOME, true);

    it("scrubs every string a method is given", () => {
        const target = channel();
        const proxy = scrubbed(target, scrub);

        proxy.appendLine("started for C:\\Users\\someone\\AppData\\x");
        proxy.error(
            "failed in C:\\Users\\someone\\a",
            "and C:\\Users\\someone\\b",
        );

        assert.deepEqual(target.written, [
            ["started for ~\\AppData\\x"],
            ["failed in ~\\a", "and ~\\b"],
        ]);
    });

    it("writes an error as its scrubbed text", () => {
        const target = channel();
        const failure = new Error("cannot read C:\\Users\\someone\\mod\\a.xml");
        failure.stack =
            "Error: cannot read C:\\Users\\someone\\mod\\a.xml\n    at C:\\Users\\someone\\ext\\out.js:1:1";

        scrubbed(target, scrub).error(failure);

        assert.deepEqual(target.written, [
            ["Error: cannot read ~\\mod\\a.xml\n    at ~\\ext\\out.js:1:1"],
        ]);
    });

    it("passes everything that is not text through as it is", () => {
        const target = channel();
        const data = { path: "C:\\Users\\someone\\kept" };
        const listener = (): void => undefined;

        scrubbed(target, scrub).error("line", 7, data, listener, null);

        assert.deepEqual(target.written, [["line", 7, data, listener, null]]);
        assert.equal(target.written[0][2], data);
    });

    it("reads a property from the channel itself", () => {
        const proxy = scrubbed(channel(), scrub);

        assert.equal(proxy.name, "C:\\Users\\someone\\named");
        assert.equal(proxy.level, 3);
    });

    it("calls a method on the channel, not on the wrapper", () => {
        const target = channel();

        assert.equal(scrubbed(target, scrub).self(), target);
    });
});
