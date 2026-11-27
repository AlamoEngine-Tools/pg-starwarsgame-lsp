// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { describe, it } from "node:test";

import { EVENT_ICONS, eventIcon, UNKNOWN_EVENT_ICON } from "./eventIcons";

/** The event types the schema declares, or null where the schema repository is not checked out. */
function schemaEventTypes(): string[] | null {
    const file = path.resolve(
        process.cwd(),
        "../../schema/eaw/enums/StoryEventType.yaml",
    );
    if (!fs.existsSync(file)) {
        return null;
    }
    return [
        ...fs
            .readFileSync(file, "utf8")
            .matchAll(/^ {2}- name:\s*"?([A-Z][A-Z0-9_]+)"?\s*$/gm),
    ].map((m) => m[1]);
}

describe("eventIcon", () => {
    it("gives an event type its own glyph", () => {
        assert.equal(eventIcon("STORY_ELAPSED"), "elapsed");
        assert.equal(eventIcon("STORY_FLAG"), "flag");
    });

    it("matches a type whatever its case, as the engine does", () => {
        assert.equal(eventIcon("Story_Elapsed"), "elapsed");
    });

    /** A schema newer than the extension may name a type this build has no glyph for. */
    it("falls back to the event's own mark for a type it does not know", () => {
        assert.equal(
            eventIcon("STORY_FROM_A_NEWER_SCHEMA"),
            UNKNOWN_EVENT_ICON,
        );
    });

    it("covers every event type the schema declares, and names none it does not", (t) => {
        const declared = schemaEventTypes();
        if (declared === null) {
            t.skip(
                "the schema repository is not checked out beside the extension",
            );
            return;
        }

        const missing = declared.filter((type) => !(type in EVENT_ICONS));
        const stray = Object.keys(EVENT_ICONS).filter(
            (type) => !declared.includes(type),
        );

        assert.deepEqual(missing, [], "event types without a glyph");
        assert.deepEqual(
            stray,
            [],
            "glyphs for event types the schema does not declare",
        );
    });
});
