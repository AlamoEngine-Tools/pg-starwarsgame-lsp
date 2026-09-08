// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Drawing with the shader the effect actually declares, rather than an archetype of it.
//
// Everything above this file is text and tables; this is where a translated effect becomes a
// three.js material. It is a `RawShaderMaterial` because three must not prepend its own boilerplate:
// the generated source is a complete GLSL ES 3.00 program with its own `main`, its own attribute
// declarations and its own uniform contract.

import * as THREE from 'three';

import type { TranslatedEffect } from './fx/effect';
import { ALPHA_TEST_OFF, ALPHA_TEST_UNIFORM } from './fx/glslProgram';
import { fitToUniform, uniformSourceFor, type UniformSource } from './fx/semantics';
import { uniformValue, type AlamoFrame } from './fx/uniforms';
import { numericParams, textureParam, type MaterialExtras } from './materials';

/**
 * Drops the `#version` line, which three.js insists on writing itself.
 *
 * `glslVersion: GLSL3` makes three prepend `#version 300 es`, and the directive has to be the first
 * thing in the source - so leaving ours in puts a second one on line 4 and nothing compiles. The
 * generated shaders keep theirs, because on their own they are complete programs and the compile
 * oracle builds them exactly as written; the quirk belongs here, at the three.js boundary.
 */
function withoutVersion(source: string): string {
    return source.replace(/^#version[^\n]*\n/, '');
}

/** A uniform this material feeds, and what feeds it. */
interface BoundUniform {
    name: string;
    source: UniformSource;
}

/** A material built from a translated effect, plus what it needs each frame. */
export class AlamoMaterial extends THREE.RawShaderMaterial {
    private readonly bound: BoundUniform[] = [];

    /** Sampler uniform name to the texture file it wants, lower-cased for matching. */
    readonly wantedTextures = new Map<string, string>();

    /** Every uniform's declared type, so no value is ever sent at the wrong width. */
    private readonly types: Map<string, string>;

    /** `Colorization`'s declared width, so the team colour is fitted like any other value. */
    private readonly colorizationType: string | undefined;

    constructor(effect: TranslatedEffect, extras: MaterialExtras) {
        super({
            vertexShader: withoutVersion(effect.vertex),
            fragmentShader: withoutVersion(effect.fragment),
            glslVersion: THREE.GLSL3,
        });

        this.types = effect.types;
        this.colorizationType = effect.types.get('Colorization');

        // The alpha test the pass declares, which reaches the GPU only this way: three's own
        // `material.alphaTest` is a built-in shader chunk, and a RawShaderMaterial gets none of
        // them. Off until an effect asks for it.
        this.uniforms[ALPHA_TEST_UNIFORM] = { value: ALPHA_TEST_OFF };

        // Three sources, weakest first.
        //
        // 1. The default the effect's own source declared. Anything nothing else feeds - fog, the
        //    light probes, the wind - then keeps a real value chosen by whoever wrote the effect
        //    rather than sitting at zero.
        for (const [name, value] of effect.defaults) {
            this.uniforms[name] = { value: fitToUniform(value, effect.types.get(name)) };
        }

        // 2. The SUB-MESH's own parameters, which is where the material properties actually live:
        //    `Diffuse`, `Specular`, `Shininess`, `Colorization`, `UVOffset` are per-sub-mesh values
        //    carried in the ALO, not engine state and not constants. Leaving them out drew the hull
        //    with whatever the header happened to declare.
        //
        //    FITTED to the uniform's declared width. The ALO stores `Diffuse` as four components
        //    while the effect declares `float3 Diffuse`, and `uniform3fv` with a 4-length array is
        //    INVALID_VALUE - GL drops the call, the uniform keeps its zero, and the model renders
        //    black with nothing anywhere reporting a problem.
        for (const [name, value] of numericParams(extras)) {
            this.uniforms[name] = { value: fitToUniform(value, effect.types.get(name)) };
        }

        // 3. The engine semantics, refreshed every frame by `updateFrame`.

        for (const [name, semantic] of effect.semantics) {
            const source = uniformSourceFor(semantic);
            if (source !== null) {
                this.uniforms[name] ??= { value: null };
                this.bound.push({ name, source });
            }
        }

        // Samplers are bound by the texture PARAMETER they read, which is how the sub-mesh names
        // its own textures. Left unresolved they would all sample whatever is on unit zero.
        for (const [sampler, parameter] of effect.samplers) {
            this.uniforms[sampler] ??= { value: null };

            const file = textureParam(extras, parameter);
            if (file === undefined) {
                this.unnamedSamplers.push(`${sampler} (${parameter})`);
            } else {
                this.wantedTextures.set(sampler, file.toLowerCase());
            }
        }
    }

    /**
     * Samplers still reading nothing, with the file each was waiting for.
     *
     * An unbound sampler reads black and the shader draws something plausible and wrong, so this is
     * worth surfacing rather than leaving to be puzzled over on screen.
     */
    unboundSamplers(): { sampler: string; file: string }[] {
        return [...this.wantedTextures]
            .filter(([sampler]) => this.uniforms[sampler]?.value === null)
            .map(([sampler, file]) => ({ sampler, file }));
    }

    /** Samplers the effect declares that no parameter on this sub-mesh names a texture for. */
    readonly unnamedSamplers: string[] = [];

    /**
     * Sets the pass's alpha cutoff, or clears it.
     *
     * Null means the effect declares no test, which is not the same as a cutoff of zero: an
     * additive glow draws where its alpha is zero, and discarding there would put it out.
     */
    setAlphaTest(threshold: number | null): void {
        this.uniforms[ALPHA_TEST_UNIFORM].value = threshold ?? ALPHA_TEST_OFF;
    }

    /** Hands a decoded texture to every sampler that asked for that file. */
    bindTexture(file: string, texture: THREE.Texture): boolean {
        const wanted = file.toLowerCase();
        let bound = false;

        for (const [sampler, want] of this.wantedTextures) {
            if (want === wanted) {
                this.uniforms[sampler].value = texture;
                bound = true;
            }
        }

        return bound;
    }

    /**
     * Sets the team colour the effect's own `Colorization` parameter carries.
     *
     * The translated shader does the masking itself - `lerp(base, Colorization * base, base.a)` -
     * so this only supplies the colour. Left alone it takes the effect's DEFAULT, which for the
     * bump shaders is a placeholder green, and every colorised model wears it.
     */
    setColorization(colour: { r: number; g: number; b: number } | null): void {
        const uniform = this.uniforms.Colorization;
        if (uniform === undefined) {
            return;
        }

        // White is the identity for the multiply, so an untinted model keeps its own texture.
        const rgb = colour === null ? [1, 1, 1] : [colour.r, colour.g, colour.b];
        uniform.value = fitToUniform([...rgb, 1], this.colorizationType);
    }

    /** Refreshes everything the engine would recompute per frame. */
    updateFrame(frame: AlamoFrame): void {
        for (const { name, source } of this.bound) {
            const value = uniformValue(source, frame);

            // Undefined means "leave the default alone" - never overwrite a real declared value
            // with a zero the preview only pretends to know.
            if (value === undefined) {
                continue;
            }

            // Fitted, exactly like the constant values. The frame supplies an eye position as three
            // components while the effects declare `float4 m_eyePos`, and `uniform4fv` with a
            // 3-length array is INVALID_VALUE - the call is dropped and the uniform silently keeps
            // whatever it had.
            this.uniforms[name].value = typeof value === 'number'
                ? value
                : fitToUniform(Array.from(value), this.types.get(name));
        }
    }
}
