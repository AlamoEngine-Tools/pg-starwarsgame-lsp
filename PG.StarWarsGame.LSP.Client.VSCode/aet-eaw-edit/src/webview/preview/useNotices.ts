// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Everything worth reporting about this preview, from every source at once, with the severity
// the tag shows and the title it carries.

import * as l10n from "@vscode/l10n";
import { useMemo } from "react";
import { type PreviewProblem } from "../../protocol/modelPreview";
import { worstSeverity } from "../loc/validateState";
import { shaderGapNotice } from "./shaderGap";
import { type ColourFinding } from "./colourUsability";

/** What useNotices reads from the rest of the preview. */
export interface NoticesInputs {
    anyShaderSource: boolean;
    attachmentIssues: string[];
    colourFindings: ColourFinding[];
    /** The Game renderer is on, and the model on stage authors no shadow mesh. */
    noShadowMesh: boolean;
    problems: PreviewProblem[];
    problemsOpen: boolean;
    shaderRequested: boolean;
    shieldOffShader: string[];
    translatedOn: boolean;
}

export type Notices = ReturnType<typeof useNotices>;

export function useNotices({
    anyShaderSource,
    attachmentIssues,
    colourFindings,
    noShadowMesh,
    problems,
    problemsOpen,
    shaderRequested,
    shieldOffShader,
    translatedOn,
}: NoticesInputs) {
    /**
     * Everything worth reporting about this preview, from every source at once.
     *
     * Derived rather than pushed into the problems state: a colour finding is a pure function of
     * the colour currently applied, so appending it to state would stack a fresh copy on every
     * click of the palette. The severity tag and the problems bar both read THIS, which is what
     * makes "where do I find out what is wrong" have one answer.
     */
    const notices = useMemo<PreviewProblem[]>(() => {
        const fromColour = colourFindings.map((finding) => ({
            severity: finding.severity,
            message: finding.message,
            hardpointId: null,
        }));

        const fromAttachments = attachmentIssues.map((message) => ({
            severity: "warning",
            message,
            hardpointId: null,
        }));

        // The shader gap is a limitation of the preview rather than a fault in the mod, so it is
        // info. The corner button is the way OUT of it; this is the explanation of it.
        const fromShaders = shaderGapNotice(
            translatedOn,
            shaderRequested,
            anyShaderSource,
        )
            ? [
                  {
                      severity: "info",
                      message: l10n.t(
                          "No shader sources are reachable, so nothing can be translated. A game install ships compiled .fxo; the .fx sources are a separate Petroglyph download.",
                      ),
                      hardpointId: null,
                  },
              ]
            : [];

        // A shield bubble the model names but does not draw with a shield shader. The NAME is what
        // decides which mesh a shield ability reveals - the author's word - so this corrects
        // nothing; it just says once that the thing will read as solid geometry rather than a
        // field.
        const fromShield =
            shieldOffShader.length === 0
                ? []
                : [
                      {
                          severity: "warning",
                          message: l10n.t(
                              "{0} {1} named as a shield mesh but not drawn with a shield shader - draws as solid geometry, not a field.",
                              shieldOffShader.join(", "),
                              shieldOffShader.length === 1 ? "is" : "are",
                          ),
                          hardpointId: null,
                      },
                  ];

        // Info, like the shader gap: the game draws this model no shadow, which is what the preview
        // now draws too. Said so a missing shadow mesh is not mistaken for a preview fault.
        const fromShadow = noShadowMesh
            ? [
                  {
                      severity: "info",
                      message: l10n.t(
                          "No shadow mesh: The game draws no shadow for this model",
                      ),
                      hardpointId: null,
                  },
              ]
            : [];

        return [
            ...problems,
            ...fromAttachments,
            ...fromColour,
            ...fromShaders,
            ...fromShadow,
            ...fromShield,
        ];
    }, [
        problems,
        attachmentIssues,
        colourFindings,
        translatedOn,
        shaderRequested,
        anyShaderSource,
        noShadowMesh,
        shieldOffShader,
    ]);

    const severity = worstSeverity(notices);
    // One catalogue text per number and state: a word joined into a sentence cannot be translated.
    const problemsTitle = problemsOpen
        ? notices.length === 1
            ? l10n.t("Hide 1 note")
            : l10n.t("Hide {0} notes", notices.length)
        : notices.length === 1
          ? l10n.t("Read 1 note")
          : l10n.t("Read {0} notes", notices.length);

    return {
        notices,
        severity,
        problemsTitle,
    };
}
