// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import "../shared/vscodeApiStandIn";

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { renderToStaticMarkup } from "react-dom/server";

import type { PreviewProblem } from "../../protocol/modelPreview";
import { type Notices, type NoticesInputs, useNotices } from "./useNotices";

function inputs(overrides: Partial<NoticesInputs>): NoticesInputs {
    return {
        anyShaderSource: false,
        attachmentIssues: [],
        colourFindings: [],
        noShadowMesh: false,
        problems: [],
        problemsOpen: false,
        shaderRequested: false,
        shieldOffShader: [],
        translatedOn: false,
        ...overrides,
    };
}

function problem(severity: string, message: string): PreviewProblem {
    return { severity, message, hardpointId: null } as PreviewProblem;
}

/** What the notices hold on the first render, through a component that only calls it. */
function firstRender(given: NoticesInputs): Notices {
    let seen: Notices | undefined;
    const Probe = (): null => {
        seen = useNotices(given);
        return null;
    };
    renderToStaticMarkup(<Probe />);
    assert.ok(seen);
    return seen;
}

describe("useNotices", () => {
    it("has nothing to report on a clean preview", () => {
        const notices = firstRender(inputs({}));

        assert.deepEqual(notices.notices, []);
        assert.equal(notices.severity, "ok");
        assert.equal(notices.problemsTitle, "Read 0 notes");
    });

    it("reports the scene's problems before the attachments, at the worst severity", () => {
        const notices = firstRender(
            inputs({
                problems: [problem("error", "From the scene")],
                attachmentIssues: ["Nowhere to hang"],
            }),
        );

        assert.deepEqual(
            notices.notices.map((n) => n.message),
            ["From the scene", "Nowhere to hang"],
        );
        assert.equal(notices.severity, "error");
        assert.equal(notices.problemsTitle, "Read 2 notes");
    });

    it("says one note in the singular, and offers to hide the notes while they are open", () => {
        const notices = firstRender(
            inputs({
                problems: [problem("info", "Only one")],
                problemsOpen: true,
            }),
        );

        assert.equal(notices.severity, "info");
        assert.equal(notices.problemsTitle, "Hide 1 note");
    });

    it("notes that the game draws no shadow for a model without a shadow mesh", () => {
        const notices = firstRender(inputs({ noShadowMesh: true }));

        assert.deepEqual(
            notices.notices.map((n) => [n.severity, n.message]),
            [
                [
                    "info",
                    "No shadow mesh: The game draws no shadow for this model",
                ],
            ],
        );
    });
});
