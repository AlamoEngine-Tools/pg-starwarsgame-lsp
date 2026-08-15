// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// One `.fx` file, translated into everything needed to draw with it.
//
// This is the boundary between the pure translation layer and the viewport: everything above is
// text and tables, everything below is three.js. Keeping the join here is what lets the whole
// translator be tested without a GPU.

import { parseFxManifest, selectTechnique } from './fxParser';
import { buildFragmentShader, buildVertexShader } from './glslProgram';
import { flattenIncludes } from './includes';
import {
    collectSamplerTextures, collectSemantics, collectUniformDefaults, collectUniformTypes,
} from './semantics';

/** A translated effect, or the reason it could not be. */
export interface TranslatedEffect {
    vertex: string;
    fragment: string;
    /** Uniform name to the engine semantic that feeds it. */
    semantics: Map<string, string>;
    /** Uniform name to the default the effect's own source declared. */
    defaults: Map<string, number[]>;
    /** Sampler uniform name to the texture PARAMETER whose value it samples. */
    samplers: Map<string, string>;
    /** Uniform name to its DECLARED type, so every value can be fitted to the width GL expects. */
    types: Map<string, string>;
}

/** What came back, and why nothing did when nothing did. */
export interface TranslationResult {
    effect: TranslatedEffect | null;
    refusal: string | null;
    /** Headers the source includes that were not supplied. Translation waits for these. */
    missingIncludes: string[];
}

/**
 * Translates one effect, given a way to read the headers it includes.
 *
 * `read` returns null for a header not yet in hand rather than throwing, so a caller fetching them
 * over a message channel can call this again as each one arrives.
 */
export function translateEffect(
    source: string, read: (name: string) => string | null,
): TranslationResult {
    const flat = flattenIncludes(source, read);
    if (flat.missing.length > 0) {
        return { effect: null, refusal: null, missingIncludes: flat.missing };
    }

    const technique = selectTechnique(parseFxManifest(source));
    const pass = technique?.passes[0];

    if (pass?.vertexShader === undefined || pass.pixelShader === undefined) {
        // Eleven of the shipped effects are render state and nothing else. Not a failure - the
        // archetype material is the whole of what they do.
        return {
            effect: null,
            refusal: 'The chosen technique sets render state only, so there is no shader to '
                + 'translate.',
            missingIncludes: [],
        };
    }

    const vertex = buildVertexShader(flat.text, pass.vertexShader);
    const fragment = buildFragmentShader(flat.text, pass.pixelShader);

    if (vertex.source === null || fragment.source === null) {
        return {
            effect: null,
            refusal: vertex.refusal ?? fragment.refusal,
            missingIncludes: [],
        };
    }

    return {
        effect: {
            vertex: vertex.source,
            fragment: fragment.source,
            semantics: collectSemantics(flat.text),
            defaults: collectUniformDefaults(flat.text),
            samplers: collectSamplerTextures(flat.text),
            // Read off the TRANSLATED source, which is where the final GLSL types live.
            types: collectUniformTypes(`${vertex.source}\n${fragment.source}`),
        },
        refusal: null,
        missingIncludes: [],
    };
}
