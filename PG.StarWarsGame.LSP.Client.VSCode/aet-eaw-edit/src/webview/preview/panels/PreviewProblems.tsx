// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The Problems band under the stage: one row per finding, its severity, where it is and its id.

import type { Dispatch, SetStateAction } from "react";
import * as l10n from "@vscode/l10n";
import { type PreviewProblem } from "../../../protocol/modelPreview";
import { SeverityMark } from "../../shared/Icon";
import { ProblemsPanel } from "../../shared/ProblemsPanel";
import {
    problemLook,
    problemTag,
    problemWhere,
} from "../../shared/problemLook";

export interface PreviewProblemsProps {
    notices: PreviewProblem[];
    setProblemsOpen: Dispatch<SetStateAction<boolean>>;
}

export function PreviewProblems({
    notices,
    setProblemsOpen,
}: PreviewProblemsProps): React.JSX.Element {
    return (
        <ProblemsPanel
            className="preview-problems problems-band prose-problems"
            memoKey="modelPreview.problems"
            defaultHeight={140}
            title={l10n.t("Problems ({0})", notices.length)}
            onClose={() => setProblemsOpen(false)}
        >
            {/* The scrolling part, and the reason the panel scrolls at all: the
                                shared sheet gives `.problem-list` the overflow and leaves the
                                title bar its height. Mapping the rows straight into the panel
                                skipped it, and the list simply ran off the bottom. */}
            <div className="problem-list">
                {notices.map((problem, index) => {
                    // Three severities, drawn as three. The ternary that used to
                    // be here sent every `info` out under the warning triangle -
                    // the one thing on the row that answers "must I fix this?".
                    const look = problemLook(problem.severity);
                    const where = problemWhere(
                        problem.hardpointId,
                        problem.message,
                    );
                    const tag = problemTag(problem.diagnosticId);

                    return (
                        <div key={index} className="problem-row">
                            <SeverityMark
                                severity={look.tone}
                                className={`sev-${look.tone}`}
                            />
                            {where !== null && (
                                <span className="problem-where">{where}</span>
                            )}
                            <span
                                className="problem-msg"
                                title={problem.message}
                            >
                                {problem.message}
                            </span>
                            {/* Last, and dim. It is what makes the finding
                                                referable, not what the reader is here to read. */}
                            <span
                                className={
                                    "problem-id" +
                                    (tag.isDiagnostic ? "" : " render-time")
                                }
                                title={tag.title}
                            >
                                {tag.text}
                            </span>
                        </div>
                    );
                })}
            </div>
        </ProblemsPanel>
    );
}
