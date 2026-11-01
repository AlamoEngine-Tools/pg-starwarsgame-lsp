// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";
import * as l10n from "@vscode/l10n";

import {
    acceptLabel,
    declineLabel,
    declinedMessage,
    diffTitle,
    outcomeOf,
    proposalMessage,
} from "./pgprojMigrationPrompt";

const proposal = {
    path: "/mods/mymod/mymod.pgproj",
    fileName: "mymod.pgproj",
    proposedText: "{}",
    notices: [] as string[],
};

describe("the proposal message", () => {
    it("names the file", () => {
        assert.match(proposalMessage(proposal), /mymod\.pgproj/);
    });

    // The one cost the diff cannot show: comments are gone once the file is rewritten, and they do
    // not appear in either side of the comparison because the parser drops them.
    it("says what will be lost and that a backup is taken", () => {
        const message = proposalMessage(proposal);

        assert.match(message, /[Cc]omments/);
        assert.match(message, /backup/);
    });

    // Declining stops the server, which is a consequence worth knowing BEFORE choosing rather than
    // discovering afterwards.
    it("says what declining does", () => {
        assert.match(proposalMessage(proposal), /stops the language server/);
    });

    it("carries each migration notice", () => {
        const message = proposalMessage({
            ...proposal,
            notices: ["Move your credits file under data/text."],
        });

        assert.match(message, /Move your credits file under data\/text\./);
    });

    it("reads cleanly with no notices", () => {
        assert.ok(
            !proposalMessage(proposal).includes("  "),
            "no double spaces where a notice would go",
        );
    });
});

describe("the answer", () => {
    it("accepts only the explicit yes", () => {
        assert.equal(outcomeOf(acceptLabel()), "accept");
    });

    // Dismissing is not "ask me later". Treating it as consent would rewrite somebody's project
    // file because they pressed Escape.
    it("treats a dismissal as a decline", () => {
        assert.equal(outcomeOf(undefined), "decline");
        assert.equal(outcomeOf(declineLabel()), "decline");
        assert.equal(outcomeOf("something else entirely"), "decline");
    });
});

describe("the diff title", () => {
    it("names the file and both sides", () => {
        assert.equal(
            diffTitle("mymod.pgproj"),
            "mymod.pgproj - on disk <-> updated format",
        );
    });
});

describe("the message after stopping", () => {
    // A server that vanished without saying why reads as a crash.
    it("says the file is untouched, that the server stopped, and how to get back", () => {
        const message = declinedMessage("mymod.pgproj");

        assert.match(message, /unchanged/);
        assert.match(message, /stopped/);
        assert.match(message, /reload the window/i);
    });
});

describe("the texts", () => {
    // The question, its buttons and the tab read in the editor's language: they come from the
    // catalogue, and the answer is still told apart in a translated editor.
    it("come from the catalogue", () => {
        l10n.config({
            contents: {
                "Update project file": "TRANSLATED ACCEPT",
                "Discard and stop": "TRANSLATED DECLINE",
                "{0} - on disk <-> updated format": "TRANSLATED TITLE {0}",
                "'{0}' uses an older project format.{1} Review the changes and update the file? Comments in it are not preserved, and a backup is written next to it first. Declining stops the language server.":
                    "TRANSLATED QUESTION {0}{1}",
                "'{0}' was left unchanged, so aet-eaw-edit has stopped. Update the project file, or reload the window to be asked again.":
                    "TRANSLATED STOPPED {0}",
            },
        });
        try {
            assert.equal(acceptLabel(), "TRANSLATED ACCEPT");
            assert.equal(declineLabel(), "TRANSLATED DECLINE");
            assert.equal(outcomeOf("TRANSLATED ACCEPT"), "accept");
            assert.equal(diffTitle("m.pgproj"), "TRANSLATED TITLE m.pgproj");
            assert.equal(
                proposalMessage({ ...proposal, notices: ["A note."] }),
                "TRANSLATED QUESTION mymod.pgproj A note.",
            );
            assert.equal(
                declinedMessage("m.pgproj"),
                "TRANSLATED STOPPED m.pgproj",
            );
        } finally {
            l10n.config({ contents: {} });
        }
    });
});
