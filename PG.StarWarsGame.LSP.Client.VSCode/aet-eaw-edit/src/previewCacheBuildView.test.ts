// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import * as assert from "node:assert/strict";
import { test } from "node:test";

import { PreviewCacheBuildView } from "./previewCacheBuildView";

/** A view over a screen that records what it was asked to draw. */
function viewWith(): {
    view: PreviewCacheBuildView;
    drawn: string[];
    cancels: () => number;
    pressCancel: () => void;
} {
    const drawn: string[] = [];
    let cancels = 0;
    let onCancel: () => void = () => undefined;

    const view = new PreviewCacheBuildView(
        (cancelled) => {
            drawn.push("begin");
            onCancel = cancelled;
            return {
                report: (message, fraction) =>
                    drawn.push(`${message} @ ${fraction.toFixed(2)}`),
                end: () => drawn.push("end"),
            };
        },
        () => cancels++,
        (done, total) => `${done} of ${total}`,
    );

    return {
        view,
        drawn,
        cancels: () => cancels,
        pressCancel: () => onCancel(),
    };
}

test("a build is one progress from its first notification to its last", () => {
    const { view, drawn } = viewWith();

    view.update({ state: "running", done: 0, total: 4 });
    view.update({ state: "running", done: 1, total: 4 });
    view.update({ state: "running", done: 4, total: 4 });
    view.update({ state: "ended", done: 4, total: 4 });

    assert.deepEqual(drawn, [
        "begin",
        "0 of 4 @ 0.00",
        "1 of 4 @ 0.25",
        "4 of 4 @ 1.00",
        "end",
    ]);
});

test("the reader's cancel is passed to the server, and the progress stays until the server says it stopped", () => {
    const { view, drawn, cancels, pressCancel } = viewWith();
    view.update({ state: "running", done: 1, total: 4 });

    pressCancel();

    assert.equal(cancels(), 1);
    assert.equal(drawn.at(-1), "1 of 4 @ 0.25");

    view.update({ state: "ended", done: 1, total: 4 });
    assert.equal(drawn.at(-1), "end");
});

test("a second build after the first is a progress of its own", () => {
    const { view, drawn } = viewWith();

    view.update({ state: "running", done: 0, total: 1 });
    view.update({ state: "ended", done: 1, total: 1 });
    view.update({ state: "running", done: 0, total: 2 });

    assert.deepEqual(
        drawn.filter((line) => line === "begin" || line === "end"),
        ["begin", "end", "begin"],
    );
});

// The total grows when a subject is opened mid-build; the bar never runs past its end.
test("the fraction stays within the bar when the total moves", () => {
    const { view, drawn } = viewWith();

    view.update({ state: "running", done: 3, total: 2 });
    view.update({ state: "running", done: 0, total: 0 });

    assert.deepEqual(drawn, ["begin", "3 of 2 @ 1.00", "0 of 0 @ 1.00"]);
});

test("an 'ended' with nothing on screen draws nothing", () => {
    const { view, drawn } = viewWith();

    view.update({ state: "ended", done: 0, total: 0 });
    view.end();

    assert.deepEqual(drawn, []);
});
