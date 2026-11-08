// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { describe, it } from "node:test";

import {
    buildWhatsNewPayload,
    classifyStart,
    compareVersions,
    currentSection,
    inlineImages,
    opensOn,
    readImageAsDataUri,
    shownVersion,
    splitChangelog,
    whatsNewOnStart,
} from "./whatsNewModel";

const changelog = [
    "# Changelog",
    "",
    "## Unreleased",
    "",
    "### Features",
    "",
    "- **Pending** - not shipped",
    "",
    "## 0.5.0",
    "",
    "### Highlights",
    "",
    "The Mod Browser lists everything a project indexes.",
    "",
    "![Mod Browser](media/whats-new/0.5.0/mod-browser.gif)",
    "",
    "### Migration",
    "",
    "| Old | New |",
    "|-----|-----|",
    "| `a.b` | `a.c` |",
    "",
    "### Features",
    "",
    "- **Mod Browser** - everything a project indexes",
    "",
    "### Fixes",
    "",
    "- **A fix** - fixed",
    "",
    "## 0.4.1",
    "",
    "### Fixes",
    "",
    "- **An older fix** - fixed earlier",
    "",
].join("\r\n");

describe("splitChangelog", () => {
    const sections = splitChangelog(changelog);

    it("yields one section per level-two heading, in file order, ignoring the title", () => {
        assert.deepEqual(
            sections.map((s) => s.version),
            ["Unreleased", "0.5.0", "0.4.1"],
        );
    });

    it("lifts the Highlights and Migration blocks out of the body", () => {
        const release = sections[1];
        assert.equal(
            release.highlights,
            "The Mod Browser lists everything a project indexes.\n\n![Mod Browser](media/whats-new/0.5.0/mod-browser.gif)",
        );
        assert.equal(
            release.migration,
            "| Old | New |\n|-----|-----|\n| `a.b` | `a.c` |",
        );
        assert.equal(
            release.body,
            "### Features\n\n- **Mod Browser** - everything a project indexes\n\n### Fixes\n\n- **A fix** - fixed",
        );
    });

    it("leaves a section without those blocks whole, and the blocks undefined", () => {
        const older = sections[2];
        assert.equal(older.highlights, undefined);
        assert.equal(older.migration, undefined);
        assert.equal(
            older.body,
            "### Fixes\n\n- **An older fix** - fixed earlier",
        );
    });

    it("reads the shipped changelog: every section is Unreleased or a three-part version", () => {
        // The tests run from the package folder, where the changelog sits; the bundle lives in out/.
        const shipped = fs.readFileSync(path.resolve("CHANGELOG.md"), "utf8");
        const real = splitChangelog(shipped);
        assert.ok(real.length >= 5);
        for (const s of real) {
            assert.match(s.version, /^(Unreleased|\d+\.\d+\.\d+)$/);
            assert.ok(s.body.length > 0, `${s.version} has an empty body`);
        }
    });
});

describe("currentSection", () => {
    const sections = splitChangelog(changelog);

    it("is the section whose heading is the installed version", () => {
        assert.equal(currentSection(sections, "0.4.1")?.version, "0.4.1");
    });

    it("falls back to the first section when no heading matches, as in a development build", () => {
        assert.equal(currentSection(sections, "0.6.0")?.version, "Unreleased");
    });

    it("is undefined for an empty changelog", () => {
        assert.equal(currentSection([], "0.5.0"), undefined);
    });
});

describe("compareVersions", () => {
    it("compares numerically per part", () => {
        assert.ok(compareVersions("0.10.0", "0.9.9") > 0);
        assert.ok(compareVersions("0.5.0", "0.5.1") < 0);
        assert.equal(compareVersions("1.2.3", "1.2.3"), 0);
    });

    it("treats a missing part as zero", () => {
        assert.equal(compareVersions("1.2", "1.2.0"), 0);
    });
});

describe("classifyStart", () => {
    it("is a first start when nothing is stored", () => {
        assert.equal(classifyStart(undefined, "0.5.0"), "first");
    });

    it("is an update when the stored version is older, patch or not", () => {
        assert.equal(classifyStart("0.4.1", "0.5.0"), "updated");
        assert.equal(classifyStart("0.5.0", "0.5.1"), "updated");
    });

    it("is the same version on a plain reload", () => {
        assert.equal(classifyStart("0.5.0", "0.5.0"), "same");
    });

    it("is a downgrade when the stored version is newer", () => {
        assert.equal(classifyStart("0.6.0", "0.5.0"), "downgraded");
    });

    it("opens the page on a first start and on an update only", () => {
        assert.equal(opensOn("first"), true);
        assert.equal(opensOn("updated"), true);
        assert.equal(opensOn("same"), false);
        assert.equal(opensOn("downgraded"), false);
    });
});

describe("buildWhatsNewPayload", () => {
    const noImages = path.join(os.tmpdir(), "whats-new-none");

    it("lists only the releases before the installed one as earlier, newest first", () => {
        assert.deepEqual(
            buildWhatsNewPayload(changelog, "0.5.0", noImages).older.map(
                (s) => s.version,
            ),
            ["0.4.1"],
        );
        assert.deepEqual(
            buildWhatsNewPayload(changelog, "0.4.1", noImages).older,
            [],
        );
    });

    it("never lists Unreleased as earlier, even when it is the fallback section", () => {
        const payload = buildWhatsNewPayload(changelog, "0.6.0", noImages);
        assert.equal(payload.current?.version, "Unreleased");
        assert.deepEqual(
            payload.older.map((s) => s.version),
            ["0.5.0", "0.4.1"],
        );
    });

    it("reduces an earlier section's images to alt text and puts its lead back as headings", () => {
        const older = buildWhatsNewPayload(changelog, "0.6.0", noImages)
            .older[0];
        assert.match(older.markdown, /^### Highlights\n\n/);
        assert.match(older.markdown, /\*Mod Browser\*/);
        assert.match(older.markdown, /### Migration\n\n\| Old \| New \|/);
        assert.match(older.markdown, /### Features\n/);
    });

    it("carries the manifest's preview flag, off unless given", () => {
        assert.equal(
            buildWhatsNewPayload(changelog, "0.5.0", noImages).preview,
            false,
        );
        assert.equal(
            buildWhatsNewPayload(changelog, "0.5.0", noImages, true).preview,
            true,
        );
    });
});

describe("whatsNewOnStart", () => {
    it("opens on a first start and an update while the setting is on, and always stores", () => {
        assert.deepEqual(whatsNewOnStart(undefined, "0.5.0", true), {
            open: true,
            store: "0.5.0",
        });
        assert.deepEqual(whatsNewOnStart("0.4.1", "0.5.0", true), {
            open: true,
            store: "0.5.0",
        });
    });

    it("stores without opening on the same version, a downgrade, or with the setting off", () => {
        assert.deepEqual(whatsNewOnStart("0.5.0", "0.5.0", true), {
            open: false,
            store: undefined,
        });
        assert.deepEqual(whatsNewOnStart("0.6.0", "0.5.0", true), {
            open: false,
            store: "0.5.0",
        });
        assert.deepEqual(whatsNewOnStart(undefined, "0.5.0", false), {
            open: false,
            store: "0.5.0",
        });
        assert.deepEqual(whatsNewOnStart("0.4.1", "0.5.0", false), {
            open: false,
            store: "0.5.0",
        });
    });
});

describe("inlineImages", () => {
    const resolve = (p: string) =>
        p === "media/a.gif" ? "data:image/gif;base64,R0lGOD" : undefined;

    it("replaces a relative image with the data URI the resolver returns", () => {
        assert.equal(
            inlineImages("See ![Alt text](media/a.gif) here", resolve),
            "See ![Alt text](data:image/gif;base64,R0lGOD) here",
        );
    });

    it("keeps a title and an absolute or data source as written", () => {
        const text =
            '![A](https://example.test/a.png "Title") ![B](data:image/png;base64,AAAA)';
        assert.equal(inlineImages(text, resolve), text);
    });

    it("reduces an image the resolver cannot read to its alt text", () => {
        assert.equal(
            inlineImages("![Missing shot](media/none.png)", resolve),
            "*Missing shot*",
        );
        assert.equal(inlineImages("![](media/none.png)", resolve), "");
    });
});

describe("readImageAsDataUri", () => {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), "whats-new-"));
    fs.mkdirSync(path.join(dir, "media"));
    fs.writeFileSync(
        path.join(dir, "media", "a.gif"),
        Buffer.from("GIF89a", "latin1"),
    );
    fs.writeFileSync(path.join(dir, "media", "a.svg"), "<svg/>");
    fs.writeFileSync(path.join(dir, "outside.png"), Buffer.from([0x89, 0x50]));

    it("reads a png, jpg, gif or webp under the base folder as a data URI", () => {
        assert.equal(
            readImageAsDataUri(path.join(dir, "media"), "a.gif"),
            `data:image/gif;base64,${Buffer.from("GIF89a", "latin1").toString("base64")}`,
        );
    });

    it("refuses an svg, which the webview guard refuses too", () => {
        assert.equal(
            readImageAsDataUri(path.join(dir, "media"), "a.svg"),
            undefined,
        );
    });

    it("refuses a path that leaves the base folder, and a file that is not there", () => {
        assert.equal(
            readImageAsDataUri(path.join(dir, "media"), "../outside.png"),
            undefined,
        );
        assert.equal(
            readImageAsDataUri(path.join(dir, "media"), "b.gif"),
            undefined,
        );
    });
});

describe("shownVersion", () => {
    it("shows the installed version to a user", () => {
        assert.equal(shownVersion("0.4.1", false), "0.4.1");
    });

    it("shows Unreleased in the development host, which is the section being written", () => {
        assert.equal(shownVersion("0.4.1", true), "Unreleased");
        const sections = splitChangelog(changelog);
        assert.equal(
            currentSection(sections, shownVersion("0.4.1", true))?.version,
            "Unreleased",
        );
    });

    it("falls back to the newest release when nothing is unreleased", () => {
        const released = splitChangelog(
            changelog.replace("## Unreleased", "## 0.5.0"),
        );
        assert.equal(
            currentSection(released, shownVersion("0.4.1", true))?.version,
            "0.5.0",
        );
    });
});
