// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
    AppliedEdit,
    EditBatchClient,
    EditBatchRequests,
    EditWorkspace,
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
        assert.deepEqual(saved, { saved: true, unsaved: [] });
    });

    it("names the files that hold the change but could not be written", async () => {
        const saved = await new EditBatchClient(
            requests({ ok: true, value: { success: true, edit: {} } }),
            workspace({ applied: true, unsaved: ["C:\\mod\\story.xml"] }),
        ).save("story", subject, commands);

        assert.deepEqual(saved, {
            saved: true,
            unsaved: ["C:\\mod\\story.xml"],
        });
    });

    it("is saved with nothing applied when the batch changes nothing", async () => {
        const editor = workspace();

        const saved = await new EditBatchClient(
            requests({ ok: true, value: { success: true, edit: null } }),
            editor,
        ).save("story", subject, commands);

        assert.deepEqual(saved, { saved: true, unsaved: [] });
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
            workspace({ applied: false, unsaved: [] }),
        ).save("story", subject, commands);

        assert.deepEqual(saved, { saved: false, reason: "notApplied" });
    });

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
