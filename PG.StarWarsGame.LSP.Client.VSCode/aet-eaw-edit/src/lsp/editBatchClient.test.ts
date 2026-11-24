// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
    AppliedEdit,
    EditBatchClient,
    EditBatchRequests,
    EditWorkspace,
    OpenDocumentState,
    unwritable,
} from "./editBatchClient";
import type { LspOutcome } from "./lspGateway";

const subject = { campaign: "GC", faction: "Rebel" };
const commands = [{ kind: "setDialog", eventName: "Start", value: "X" }];

/** Requests that answer with one outcome and remember what was asked. */
function requests(outcome: LspOutcome<unknown>): EditBatchRequests & {
    sent: { method: string; params: unknown }[];
} {
    const sent: { method: string; params: unknown }[] = [];
    return {
        sent,
        request: <T>(method: string, params?: unknown) => {
            sent.push({ method, params });
            return Promise.resolve(outcome as LspOutcome<T>);
        },
    };
}

/** A workspace that answers with one result and remembers the edits it was handed. */
function workspace(
    result: AppliedEdit = { applied: true, unsaved: [] },
): EditWorkspace & { edits: unknown[] } {
    const edits: unknown[] = [];
    return {
        edits,
        applyAndSave: (edit: unknown) => {
            edits.push(edit);
            return Promise.resolve(result);
        },
    };
}

describe("EditBatchClient.validate", () => {
    it("sends the batch in the one envelope and hands the answer back", async () => {
        const server = requests({
            ok: true,
            value: { problems: [{ severity: "error", message: "bad" }] },
        });

        const outcome = await new EditBatchClient(server, workspace()).validate(
            "story",
            subject,
            commands,
        );

        assert.deepEqual(server.sent, [
            {
                method: "aet/validateEditBatch",
                params: { editor: "story", subject, commands },
            },
        ]);
        assert.ok(outcome.ok);
        assert.equal(outcome.value.problems[0].message, "bad");
    });

    it("applies nothing", async () => {
        const editor = workspace();

        await new EditBatchClient(
            requests({ ok: true, value: { problems: [] } }),
            editor,
        ).validate("story", subject, commands);

        assert.deepEqual(editor.edits, []);
    });
});

describe("EditBatchClient.save", () => {
    it("sends the batch, then hands the composed edit to the editor to apply and save", async () => {
        const edit = { documentChanges: [] };
        const server = requests({ ok: true, value: { success: true, edit } });
        const editor = workspace();

        const saved = await new EditBatchClient(server, editor).save(
            "story",
            subject,
            commands,
        );

        assert.deepEqual(server.sent, [
            {
                method: "aet/applyEditBatch",
                params: { editor: "story", subject, commands },
            },
        ]);
        assert.deepEqual(editor.edits, [edit]);
        assert.deepEqual(saved, { saved: true, unsaved: [], documents: [] });
    });

    it("hands back each file's hash once the batch is written", async () => {
        const documents = [
            { filePath: "C:\\mod\\texts.csv", contentHash: "abc" },
        ];

        const saved = await new EditBatchClient(
            requests({
                ok: true,
                value: { success: true, edit: {}, documents },
            }),
            workspace(),
        ).save("translation", { documents }, commands);

        assert.deepEqual(saved, { saved: true, unsaved: [], documents });
    });

    it("names the files that hold the change but could not be written", async () => {
        const saved = await new EditBatchClient(
            requests({ ok: true, value: { success: true, edit: {} } }),
            workspace({ applied: true, unsaved: ["C:\\mod\\story.xml"] }),
        ).save("story", subject, commands);

        assert.deepEqual(saved, {
            saved: true,
            unsaved: ["C:\\mod\\story.xml"],
            documents: [],
        });
    });

    it("is saved with nothing applied when the batch changes nothing", async () => {
        const editor = workspace();

        const saved = await new EditBatchClient(
            requests({ ok: true, value: { success: true, edit: null } }),
            editor,
        ).save("story", subject, commands);

        assert.deepEqual(saved, { saved: true, unsaved: [], documents: [] });
        assert.deepEqual(editor.edits, []);
    });

    it("applies nothing when the server refuses the batch, and names the command", async () => {
        const editor = workspace();

        const saved = await new EditBatchClient(
            requests({
                ok: true,
                value: {
                    success: false,
                    failedIndex: 2,
                    error: "no such event",
                },
            }),
            editor,
        ).save("story", subject, commands);

        assert.deepEqual(saved, {
            saved: false,
            reason: "refused",
            message: "no such event",
            failedIndex: 2,
        });
        assert.deepEqual(editor.edits, []);
    });

    it("says so when the editor does not take the edit", async () => {
        const saved = await new EditBatchClient(
            requests({ ok: true, value: { success: true, edit: {} } }),
            workspace({ applied: false, reason: "refused", files: [] }),
        ).save("story", subject, commands);

        assert.deepEqual(saved, { saved: false, reason: "notApplied" });
    });

    for (const reason of ["changedSince", "unsavedChanges"] as const) {
        it(`names the files when the window holds back for ${reason}`, async () => {
            const saved = await new EditBatchClient(
                requests({ ok: true, value: { success: true, edit: {} } }),
                workspace({
                    applied: false,
                    reason,
                    files: ["C:\\mod\\texts.csv"],
                }),
            ).save("translation", subject, commands);

            assert.deepEqual(saved, {
                saved: false,
                reason,
                files: ["C:\\mod\\texts.csv"],
            });
        });
    }

    for (const reason of ["offline", "failed"] as const) {
        it(`applies nothing when the request comes back ${reason}`, async () => {
            const editor = workspace();

            const saved = await new EditBatchClient(
                requests({ ok: false, reason, message: "why" }),
                editor,
            ).save("story", subject, commands);

            assert.deepEqual(saved, { saved: false, reason, message: "why" });
            assert.deepEqual(editor.edits, []);
        });
    }
});

describe("unwritable", () => {
    /** An edit replacing one file, composed from that version, or from the saved file when none. */
    function replacing(uri: string, version?: number | null): unknown {
        return { textDocument: { uri, version }, edits: [{ newText: "x" }] };
    }

    function window(
        open: Record<string, OpenDocumentState>,
    ): (uri: string) => OpenDocumentState | undefined {
        return (uri) => open[uri];
    }

    it("lets an edit composed from the saved file onto a file that is not open", () => {
        assert.equal(
            unwritable({ documentChanges: [replacing("a", null)] }, window({})),
            undefined,
        );
    });

    it("lets an edit composed from the saved file onto a document with nothing unsaved", () => {
        assert.equal(
            unwritable(
                { documentChanges: [replacing("a")] },
                window({ a: { version: 3, dirty: false } }),
            ),
            undefined,
        );
    });

    it("holds back an edit composed from the saved file while the document has unsaved changes", () => {
        assert.deepEqual(
            unwritable(
                {
                    documentChanges: [
                        replacing("a", null),
                        replacing("b", null),
                    ],
                },
                window({
                    a: { version: 3, dirty: true },
                    b: { version: 1, dirty: false },
                }),
            ),
            { reason: "unsavedChanges", uris: ["a"] },
        );
    });

    it("lets an edit composed from an open document onto that document at that version, unsaved or not", () => {
        assert.equal(
            unwritable(
                { documentChanges: [replacing("a", 7)] },
                window({ a: { version: 7, dirty: true } }),
            ),
            undefined,
        );
    });

    it("holds back an edit composed from a version the document has left", () => {
        assert.deepEqual(
            unwritable(
                { documentChanges: [replacing("a", 7)] },
                window({ a: { version: 8, dirty: true } }),
            ),
            { reason: "changedSince", uris: ["a"] },
        );
    });

    it("holds back an edit composed from a document the window no longer holds", () => {
        assert.deepEqual(
            unwritable({ documentChanges: [replacing("a", 7)] }, window({})),
            { reason: "changedSince", uris: ["a"] },
        );
    });

    it("says a document moved on before it says one has unsaved changes", () => {
        assert.deepEqual(
            unwritable(
                { documentChanges: [replacing("a", null), replacing("b", 2)] },
                window({
                    a: { version: 1, dirty: true },
                    b: { version: 3, dirty: false },
                }),
            ),
            { reason: "changedSince", uris: ["b"] },
        );
    });

    it("passes over what is not a replacement of a file's text", () => {
        assert.equal(
            unwritable(
                { documentChanges: [{ kind: "create", uri: "new" }] },
                window({}),
            ),
            undefined,
        );
    });

    it("takes an edit with no changes at all", () => {
        assert.equal(unwritable({}, window({})), undefined);
    });
});
