// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// How a pass's declared render state - its blend and its depth handling - is expressed in three.
//
// One place, because there are two routes to a material - the archetype guessed from a shader's
// NAME, and the render state read out of the effect itself - and they have to land on the same
// blend. They did not: both said "additive" and both used three's AdditiveBlending, which is
// SRC_ALPHA, ONE.
//
// That matters because Alamo's additive effects are ONE, ONE. The two are identical on any texture
// with a sensible alpha channel and completely different on one without: `Ni_geonosian.dds` has an
// EMPTY alpha channel - measured, 4092 of its 4096 DXT5 blocks carry alpha endpoints (0, 1) - so
// scaling by alpha multiplied its wings away entirely. The engine adds them at full strength, which
// is why the model has wings in game and had none here.

import * as THREE from 'three';

import { type FxBlend, type FxDepthFunc, type FxMaterialState } from './fx/renderState';

/**
 * Puts an Alamo blend onto a three material.
 *
 * `custom` is deliberately a no-op: the effect declared a combination this does not model, and
 * whatever the archetype already chose is a better answer than a wrong translation of it.
 */
export function applyBlend(material: THREE.Material, blend: FxBlend): void {
    switch (blend) {
        case 'opaque':
            material.transparent = false;
            material.blending = THREE.NormalBlending;
            break;

        case 'alpha':
            material.transparent = true;
            material.blending = THREE.NormalBlending;
            break;

        case 'additive':
            // ONE, ONE - the source added as it is. NOT three's AdditiveBlending.
            material.transparent = true;
            material.blending = THREE.CustomBlending;
            material.blendEquation = THREE.AddEquation;
            material.blendSrc = THREE.OneFactor;
            material.blendDst = THREE.OneFactor;
            break;

        case 'additiveAlpha':
            // SRCALPHA, ONE - which is exactly what three calls AdditiveBlending.
            material.transparent = true;
            material.blending = THREE.AdditiveBlending;
            break;

        case 'multiply':
            material.transparent = true;
            material.blending = THREE.MultiplyBlending;
            break;

        default:
            break;
    }
}

/** Alamo's depth comparisons, in three's terms. */
const DEPTH_FUNCS: Record<FxDepthFunc, THREE.DepthModes> = {
    less: THREE.LessDepth,
    lessEqual: THREE.LessEqualDepth,
    greater: THREE.GreaterDepth,
    greaterEqual: THREE.GreaterEqualDepth,
    equal: THREE.EqualDepth,
    always: THREE.AlwaysDepth,
};

/**
 * Puts a pass's depth handling onto a three material.
 *
 * Includes the comparison the effect declared, which was read into `FxMaterialState` from the start
 * and then never applied to anything. Only `MeshOccludedUnit` and its RSkin twin declare anything
 * but LESSEQUAL - both gated off as non-visual - so it has been inert rather than wrong, but a mod
 * shader asking for GREATER was being silently ignored.
 *
 * NO depth bias is applied here, and that is deliberate rather than an omission. Not one shipped
 * Alamo effect declares one: the light meshes that sit directly on a hull are `ZWriteEnable = FALSE`
 * with `ZFunc = LESSEQUAL`, and the engine relies on plain depth precision to hold them apart. A
 * bias was tried and measured against the Victory Star Destroyer's `Lighting` mesh, which is the
 * worst case to hand: it changed nothing at all, from two units up to two hundred and fifty six.
 * Whatever residual instability that mesh shows is therefore NOT a depth tie - an overlay winning
 * every comparison outright still flickered by the same amount - so a bias would have been a change
 * with a reason attached to it that was not true.
 */
export function applyDepth(material: THREE.Material, state: Pick<FxMaterialState,
    'depthTest' | 'depthWrite' | 'depthFunc'>): void {
    material.depthTest = state.depthTest;
    material.depthWrite = state.depthWrite;
    material.depthFunc = DEPTH_FUNCS[state.depthFunc];
}

/**
 * How opaque the shadow-map catcher must be to darken the ground by the reader's multiplier.
 *
 * The shadow value is a MULTIPLIER - `DEFAULT_LIGHTS.shadow` is `[0.5, 0.5, 0.5]`, "half as bright",
 * and that is how the engine's stencil darken uses it. But the shadow-MAP fallback is a
 * `THREE.ShadowMaterial`, which PAINTS its colour rather than multiplying, so handing it the same
 * value painted mid grey onto a dark floor and produced a shadow BRIGHTER than the ground it fell
 * on. It is the same error the stencil path already had and fixed: the number was never wrong, the
 * blend was.
 *
 * A paint expresses a NEUTRAL multiply exactly. With normal blending the result is
 * `src * a + dst * (1 - a)`, so black at `a = 1 - m` gives `dst * m`. The catcher therefore paints
 * black and this decides the alpha - and a black paint can never be brighter than what it falls on,
 * whatever the reader picks.
 *
 * **A TINTED shadow loses its tint here**, and cannot keep it: matching `dst * m` per channel needs
 * an alpha per channel, which blending does not have. The tint still reaches the model through the
 * stencil darken, which really does multiply; this is the fallback for the 63% of models that
 * author no volume at all.
 *
 * Read as WRITTEN, not as three decodes it. `new THREE.Color('#808080').r` is **0.216** - the linear
 * value - and using it would darken to a fifth where the reader asked for a half. That exact trap
 * cost the stencil darken a cycle already.
 */
export function shadowCatcherOpacity(multiplier: THREE.ColorRepresentation): number {
    const hex = new THREE.Color(multiplier).getHex(THREE.SRGBColorSpace);

    const luminance = (0.2126 * ((hex >> 16) & 0xff)
        + 0.7152 * ((hex >> 8) & 0xff)
        + 0.0722 * (hex & 0xff)) / 0xff;

    return Math.min(1, Math.max(0, 1 - luminance));
}

/**
 * Decides whether a base map is DECODED when it is sampled.
 *
 * The engine does no colour management at all, and says so outright: `Engine/Global.fx` sets
 * `SRGBTexture = false` in its global sampler block, AloViewer sets no sRGB sampler or write state
 * anywhere, keeps every surface a plain `D3DFMT_A8R8G8B8`, and installs no gamma ramp. It samples
 * the bytes as stored, shades on them, and writes them straight out.
 *
 * A TRANSLATED effect runs that same HLSL, so it has to be given the same values. It also never
 * encodes on the way out - three deliberately adds no colour-space chunk to a `RawShaderMaterial`,
 * and the GLSL builder emits none - so decoding on the way IN was the whole of the error: linear
 * values written into a buffer read as sRGB. Measured on `UB_Palace_ALT0`: mean luminance 22.1
 * decoded against 64.6 raw, where the texture's own mean is 71.1.
 *
 * Three's OWN materials keep their decode. That path is a deliberate approximation - it is what
 * renders for anyone without their own copy of the base shaders, which are Petroglyph's and are
 * never shipped - and decode/light/encode is self-consistent: it agrees with the engine wherever the
 * operation is just "show this texture", and only multiplies pull the two apart.
 *
 * **One texture, one answer, decided from the whole model.** `viewport.textures` caches a single
 * `THREE.Texture` per NAME and shares it across every mesh that names it, so a model drawing one
 * texture from both a fixed-function effect and a programmable one cannot have it both ways - 10 of
 * the 1957 shipped models do exactly that, and the decode wins for them. The caller must therefore
 * answer for the MODEL, not for the material in its hand. This is a pure function of that answer in
 * both directions, which is what the ordering demands: textures arrive and bind while the materials
 * are still archetypes, and the translated ones only replace them once the shaders turn up.
 */
export function applyBaseMapColourSpace(texture: THREE.Texture, usedByArchetype: boolean): void {
    const wanted = usedByArchetype ? THREE.SRGBColorSpace : THREE.NoColorSpace;

    if (texture.colorSpace === wanted) {
        return;
    }

    texture.colorSpace = wanted;

    // The colour space picks the GPU's internal format, so an already-uploaded texture has to be
    // sent again. Without this the mark moves and nothing changes on screen - which is exactly what
    // the first live run of this showed, the texture reading raw and the frame still rendering
    // decoded. Only on a real change, so the common path re-uploads nothing.
    texture.needsUpdate = true;
}

/**
 * Scales a material's albedo by what the fixed-function first texture stage declares.
 *
 * `ColorOp[0] = MODULATE2X` DOUBLES the modulated colour. It is the same statement the programmable
 * path makes as `texel.rgb * In.Diff * 2.0f`, and for the four shipped effects whose shaders sit
 * inside a block comment - `MeshAlpha.fx` above all, worn by 286 models and 1282 sub-meshes - the
 * declaration is the only place the doubling is written down at all. Nothing read it, so every one
 * of those meshes drew at half its intended brightness.
 *
 * A UNIFORM rather than `material.color`, because two other places set that colour to white
 * outright - the colorization tint, and the moment a base texture binds - so a multiplier living
 * there would be quietly reset by whichever ran last.
 *
 * The injection CHAINS onto whatever was already installed instead of replacing it. A mesh can need
 * both this and the colorization tint: `colorize` is true for any mesh wearing the faction-colour
 * prefix whatever its effect is, so the two genuinely overlap, and a plain assignment would drop
 * the tint on exactly those meshes.
 */
export function applyColourScale(material: THREE.Material, scale: number): void {
    const data = material.userData as { colourScale?: { value: number } };

    if (data.colourScale !== undefined) {
        data.colourScale.value = scale;
        return;
    }

    // Nothing to install for the ordinary case: a second compiled program to multiply by one is a
    // cost with no effect.
    if (scale === 1) {
        return;
    }

    const uniform = { value: scale };
    data.colourScale = uniform;

    const earlier = material.onBeforeCompile;
    const earlierKey = material.customProgramCacheKey;

    material.onBeforeCompile = (shader, renderer) => {
        earlier.call(material, shader, renderer);

        shader.uniforms.aetColourScale = uniform;

        // After `map_fragment`, so it scales the albedo the texture has already been multiplied
        // into - which is what the stage's own result is. Unguarded by USE_MAP: the stage doubles
        // whatever it produced, and a mesh with no base texture modulates against white.
        shader.fragmentShader = shader.fragmentShader
            .replace('void main() {', 'uniform float aetColourScale;\nvoid main() {')
            .replace('#include <map_fragment>',
                '#include <map_fragment>\n    diffuseColor.rgb *= aetColourScale;');
    };

    // Composed with whatever key was already there, so a material carrying both injections cannot
    // share a compiled program with one carrying only the other.
    material.customProgramCacheKey = () => `${earlierKey.call(material)}|aet-scale`;
}
