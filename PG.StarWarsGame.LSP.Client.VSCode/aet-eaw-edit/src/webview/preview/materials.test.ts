// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import {
    collectTextureNames, debugColour, isShieldMesh, isStealthMesh, isVisibleAt, isVisibleAtLevel,
    numericParams, resolveMaterial, shieldMeshOffShader, textureParam,
    type MaterialExtras,
    castsShadowMap,
} from './materials';

/** The extras the exporter writes for one sub-mesh. */
function extras(shader: string, rest: Partial<MaterialExtras> = {}): MaterialExtras {
    return { alamoShader: shader, alamoMesh: 'HULL', ...rest };
}

describe('resolveMaterial', () => {
    /**
     * Shadow volumes and collision hulls are geometry the engine consumes, not shows. Drawing them
     * wraps the model in a black shell, which reads as a broken preview rather than extra detail.
     */
    it('hides geometry the engine never shows', () => {
        for (const shader of [
            'MeshShadowVolume.fx', 'RSkinShadowVolume.fx', 'MeshCollision.fx',
            'MeshOccludedUnit.fx', 'MeshHeat.fx', 'MeshLightVisualize.fx',
        ]) {
            assert.equal(resolveMaterial(extras(shader)).hidden, true, shader);
        }
    });

    it('treats the additive family as additive and stops it writing depth', () => {
        for (const shader of ['MeshAdditive.fx', 'RSkinAdditiveVColor.fx', 'MeshAdditiveOffset.fx']) {
            const spec = resolveMaterial(extras(shader));

            assert.equal(spec.blend, 'additive', shader);
            // An additive glow that writes depth punches a hole in whatever is drawn after it.
            assert.equal(spec.depthWrite, false, shader);
        }
    });

    it('treats the alpha family as alpha blended', () => {
        assert.equal(resolveMaterial(extras('MeshAlpha.fx')).blend, 'alpha');
        assert.equal(resolveMaterial(extras('RSkinAlphaGloss.fx')).blend, 'alpha');
    });

    /**
     * All six shipped alpha effects set `ZWriteEnable = FALSE` in every technique - checked one by
     * one across MeshAlpha, MeshAlphaGloss, MeshAlphaScroll, BatchMeshAlpha, RSkinAlpha and
     * RSkinAlphaGloss, which between them declare 19 techniques and not one with it on.
     *
     * Writing depth from a blended mesh is what leaves the SILHOUETTE of its texture hanging in the
     * air: the fully transparent texels still fill the depth buffer, so everything drawn behind them
     * afterwards is rejected and the cut-out shows as a faint outline.
     */
    it('stops the alpha family writing depth', () => {
        for (const shader of ['MeshAlpha.fx', 'MeshAlphaGloss.fx', 'MeshAlphaScroll.fx',
            'BatchMeshAlpha.fx', 'RSkinAlpha.fx', 'RSkinAlphaGloss.fx']) {
            assert.equal(resolveMaterial(extras(shader)).depthWrite, false, shader);
        }
    });

    /**
     * `Additive.fxh` is `return texel * In.Diff`, where `In.Diff` is a constant colour times a
     * global light scale - no normal, no lambert term, no specular. Lighting it with a standard
     * material adds a dielectric specular lobe that a black albedo does NOT suppress, about 4% grey,
     * and additive blending turns that into a visible rectangle wherever the flare texture is black.
     * That is the muzzle-flash quad drawing as a grey box around its flare.
     */
    it('marks the additive family unlit, because the effect does no lighting', () => {
        for (const shader of ['MeshAdditive.fx', 'RSkinAdditiveVColor.fx', 'MeshAdditiveOffset.fx']) {
            assert.equal(resolveMaterial(extras(shader)).lit, false, shader);
        }
    });

    it('keeps ordinary geometry lit', () => {
        assert.equal(resolveMaterial(extras('MeshBumpColorize.fx')).lit, true);
        assert.equal(resolveMaterial(extras('MeshAlpha.fx')).lit, true);
        assert.equal(resolveMaterial(extras('SomeModsOwnShader.fx')).lit, true);
    });

    describe('recognising a collision hull', () => {
        /**
         * The shader is the clearest signal and covers 563 sub-meshes across the shipped trees.
         */
        it('recognises one by its shader', () => {
            const spec = resolveMaterial(extras('MeshCollision.fx'));

            assert.equal(spec.archetype, 'collision');
            assert.equal(spec.hidden, true);
        });

        /**
         * But 187 more are collision hulls with NO collision shader - 174 of them on `alDefault.fx`,
         * which is simply "no shader assigned". `Rv_nebulonb.alo` is one: a mesh called `COLLISION`,
         * marked hidden in the file, left on the default shader. Those drew as ordinary grey
         * geometry when switched on rather than as a blue hull.
         *
         * HIDDEN is required as well as the name. 186 of the 187 are hidden, and demanding it keeps
         * the rule off any visible mesh that merely has the letters in its name.
         */
        it('recognises a hidden one that was left on the default shader', () => {
            const spec = resolveMaterial({
                alamoShader: 'alDefault.fx', alamoMesh: 'COLLISION', alamoHidden: true,
            });

            assert.equal(spec.archetype, 'collision');
            assert.equal(spec.hidden, true);
        });

        it('accepts the suffixed spelling the station parts use', () => {
            // `HP04_LC_Coll`, `HP01_CM_Coll` and 150-odd siblings.
            assert.equal(resolveMaterial({
                alamoShader: 'alDefault.fx', alamoMesh: 'HP04_LC_Coll', alamoHidden: true,
            }).archetype, 'collision');
        });

        it('leaves hidden geometry that is not named like collision alone', () => {
            // `B_Base`, `blink lights`, `ELEC_BOX_ALT2`, `Scale` - all hidden, none collision.
            for (const mesh of ['B_Base', 'blink lights', 'Scale', 'ELEC_BOX_ALT2']) {
                assert.equal(resolveMaterial({
                    alamoShader: 'alDefault.fx', alamoMesh: mesh, alamoHidden: true,
                }).archetype, 'opaque', mesh);
            }
        });

        it('leaves a VISIBLE mesh alone however it is named', () => {
            assert.equal(resolveMaterial({
                alamoShader: 'alDefault.fx', alamoMesh: 'Collector_Dish',
            }).hidden, false);
        });

        it('keeps a shadow volume distinct from a collision hull', () => {
            assert.equal(resolveMaterial(extras('RSkinShadowVolume.fx')).archetype, 'shadow-volume');
            // `Rv_transport.alo` has one called `shadow-Collision`; the shader decides, not the name.
            assert.equal(resolveMaterial({
                alamoShader: 'MeshShadowVolume.fx', alamoMesh: 'shadow-Collision',
            }).archetype, 'shadow-volume');
        });
    });

    it('falls back to opaque for a shader it has never seen', () => {
        // A mod's own .fx must still draw. Silently hiding it would look like a broken model.
        const spec = resolveMaterial(extras('SomeModsOwnShader.fx'));

        assert.equal(spec.hidden, false);
        assert.equal(spec.blend, 'opaque');
        assert.equal(spec.archetype, 'opaque');
    });

    it('reports an unknown archetype when there is no shader name at all', () => {
        assert.equal(resolveMaterial({}).archetype, 'unknown');
    });

    describe('team colour', () => {
        it('applies to a Colorize shader', () => {
            assert.equal(resolveMaterial(extras('MeshBumpColorize.fx')).colorize, true);
            assert.equal(resolveMaterial(extras('RSkinGlossColorize.fx')).colorize, true);
        });

        /**
         * The engine also colours any mesh named FC_*, whatever its shader. Checking only the shader
         * name misses the faction stripes authored that way.
         */
        it('applies to an FC_ mesh on an ordinary shader', () => {
            assert.equal(
                resolveMaterial(extras('MeshAlpha.fx', { alamoMesh: 'FC_Stripe' })).colorize, true);
        });

        it('does not apply to an ordinary mesh on an ordinary shader', () => {
            assert.equal(resolveMaterial(extras('MeshAlpha.fx')).colorize, false);
        });

        it('never applies to geometry that is not drawn', () => {
            const spec = resolveMaterial(extras('MeshShadowVolume.fx', { alamoMesh: 'FC_Stripe' }));

            assert.equal(spec.colorize, false);
        });
    });

    describe('texture slots', () => {
        it('binds each parameter to the slot the renderer uses it for', () => {
            const spec = resolveMaterial(extras('MeshBumpColorize.fx', {
                'param:BaseTexture': 'AI_Rancor.tga',
                'param:NormalTexture': 'AI_Rancor_B.tga',
            }));

            assert.equal(spec.textures.base, 'AI_Rancor.tga');
            assert.equal(spec.textures.normal, 'AI_Rancor_B.tga');
            assert.equal(spec.textures.gloss, undefined);
        });

        /** Parameter names are not consistently cased across the shipped files and mods. */
        it('matches parameter names case-insensitively', () => {
            const spec = resolveMaterial(extras('MeshAlpha.fx', { 'param:basetexture': 'hull.tga' }));

            assert.equal(spec.textures.base, 'hull.tga');
        });

        it('ignores a parameter that is not a texture name', () => {
            const spec = resolveMaterial(extras('MeshAlpha.fx', { 'param:BaseTexture': 3 }));

            assert.equal(spec.textures.base, undefined);
        });
    });
});

describe('textureParam', () => {
    it('does not confuse a shader parameter with the metadata beside it', () => {
        // Parameters are namespaced precisely so a mod may name one "alamoShader" without collision.
        const spec = { alamoShader: 'MeshAlpha.fx', 'param:alamoShader': 'sneaky.tga' };

        assert.equal(textureParam(spec, 'alamoShader'), 'sneaky.tga');
    });

    it('returns undefined for an absent parameter', () => {
        assert.equal(textureParam({}, 'BaseTexture'), undefined);
    });
});

describe('isVisibleAt', () => {
    /** Untagged geometry is the hull itself; hiding it would show only the damage states. */
    it('always draws geometry with no ALT or LOD tag', () => {
        assert.equal(isVisibleAt({}, 0, 0), true);
        assert.equal(isVisibleAt({}, 5, 3), true);
    });

    it('draws a tagged mesh only at its own level', () => {
        assert.equal(isVisibleAt({ alamoAlt: 2 }, 2, 0), true);
        assert.equal(isVisibleAt({ alamoAlt: 2 }, 0, 0), false);
        assert.equal(isVisibleAt({ alamoLod: 1 }, 0, 1), true);
        assert.equal(isVisibleAt({ alamoLod: 1 }, 0, 0), false);
    });

    it('requires both levels to match when both are tagged', () => {
        assert.equal(isVisibleAt({ alamoAlt: 2, alamoLod: 1 }, 2, 1), true);
        assert.equal(isVisibleAt({ alamoAlt: 2, alamoLod: 1 }, 2, 0), false);
    });

    it('respects a mesh the file marks hidden', () => {
        assert.equal(isVisibleAt({ alamoHidden: true }, 0, 0), false);
    });
});

describe('collectTextureNames', () => {
    it('asks for each distinct texture once', () => {
        const names = collectTextureNames([
            extras('MeshAlpha.fx', { 'param:BaseTexture': 'hull.tga' }),
            extras('MeshAlpha.fx', { 'param:BaseTexture': 'hull.tga' }),
            extras('MeshBumpColorize.fx', {
                'param:BaseTexture': 'hull.tga', 'param:NormalTexture': 'hull_b.tga',
            }),
        ]);

        assert.deepEqual(names, ['hull.tga', 'hull_b.tga']);
    });

    /** A capital ship's shadow volume would otherwise pull a texture nothing ever samples. */
    it('skips textures of geometry that is not drawn', () => {
        const names = collectTextureNames([
            extras('MeshShadowVolume.fx', { 'param:BaseTexture': 'never_drawn.tga' }),
        ]);

        assert.deepEqual(names, []);
    });
});

describe('numericParams', () => {
    // A sub-mesh carries its own material properties, and they are the ones that actually differ
    // between two meshes sharing an effect: Diffuse, Specular, Shininess, Colorization, UVOffset.
    // The Star Destroyer's hull declares all five.
    //
    // The SHAPES here are the exporter's, read off `ModelGlbExporter.ParameterValue`: a Float3 or
    // Float4 becomes a JSON array and a Float or Int becomes a JSON number. An earlier fixture
    // guessed comma-joined strings, and because the parser was written to match the fixture rather
    // than the file, every numeric parameter of every sub-mesh was silently dropped - the collision
    // hull's blue among them. Same mistake as the sampler fixture that guessed angle brackets.
    const EXTRAS = {
        alamoShader: 'MeshBumpColorize.fx',
        'param:Emissive': [0, 0, 0, 0],
        'param:Diffuse': [1, 1, 1, 0],
        'param:Shininess': 32,
        'param:Colorization': [1, 1, 1, 0],
        'param:BaseTexture': 'EV_StarDestroyer.TGA',
        'param:NormalTexture': 'EV_StarDestroyer_BC.tga',
    };

    it('reads a vector parameter into its components', () => {
        assert.deepEqual(numericParams(EXTRAS).get('Diffuse'), [1, 1, 1, 0]);
        assert.deepEqual(numericParams(EXTRAS).get('Emissive'), [0, 0, 0, 0]);
    });

    it('reads a scalar as a single component', () => {
        assert.deepEqual(numericParams(EXTRAS).get('Shininess'), [32]);
    });

    it('keeps the parameter name as declared, since that is the uniform`s name', () => {
        assert.equal(numericParams(EXTRAS).has('Colorization'), true);
        assert.equal(numericParams(EXTRAS).has('param:Colorization'), false);
    });

    it('leaves texture parameters out, because those are file names not numbers', () => {
        assert.equal(numericParams(EXTRAS).has('BaseTexture'), false);
        assert.equal(numericParams(EXTRAS).has('NormalTexture'), false);
    });

    it('ignores the extras that are not parameters at all', () => {
        assert.equal(numericParams(EXTRAS).has('alamoShader'), false);
        assert.equal(numericParams({ alamoAlt: 2 }).size, 0);
    });

    /**
     * A mod's exporter, or a hand-edited glb, may well write the vector as text. Reading both costs
     * one branch and means the renderer never silently loses a parameter over its spelling.
     */
    it('still reads a comma-joined string', () => {
        assert.deepEqual(
            numericParams({ 'param:Diffuse': '1, 0.5, 0.25, 1' }).get('Diffuse'),
            [1, 0.5, 0.25, 1]);
    });

    it('drops an array that is not all numbers', () => {
        assert.equal(numericParams({ 'param:Odd': [1, 'x'] }).has('Odd'), false);
    });
});

describe('debugColour', () => {
    /**
     * The one that was actually wrong on screen: `MeshCollision.fx` declares
     * `Color = {0, 0, 1, 0.5}` and every shipped collision hull writes it explicitly, so a hull
     * forced visible has to come out BLUE. It came out cyan - the fallback - because the parameter
     * arrives as an array and the parser only read strings.
     */
    it('reads the collision hull`s blue off the sub-mesh', () => {
        assert.deepEqual(
            debugColour({ alamoShader: 'MeshCollision.fx', 'param:Color': [0, 0, 1, 0.5] }),
            { r: 0, g: 0, b: 1, a: 0.5 });
    });

    it('reads a shadow volume`s DebugColor', () => {
        assert.deepEqual(
            debugColour({ alamoShader: 'RSkinShadowVolume.fx', 'param:DebugColor': [0, 1, 1, 1] }),
            { r: 0, g: 1, b: 1, a: 1 });
    });

    it('gives nothing back when the sub-mesh declares no colour', () => {
        assert.equal(debugColour({ alamoShader: 'MeshCollision.fx' }), null);
    });
});

describe('collectTextureNames, beyond the archetype slots', () => {
    // The archetype only ever samples base, normal and gloss, so those were the only names fetched.
    // A translated shader samples whatever the effect declares - MeshShield reads a wave and a
    // ripple texture - and an unfetched one leaves its sampler reading black.
    it('asks for every texture the sub-mesh names, not just the three slots', () => {
        const names = collectTextureNames([{
            alamoShader: 'MeshShield.fx',
            'param:BaseTexture': 'shield_color.tga',
            'param:WaveTexture': 'NB_ShieldWave.tga',
            'param:DistortionTexture': 'NB_ShieldRipple.tga',
            'param:EdgeBrightness': '0.5',
            'param:BaseUVScale': '16',
        }]);

        assert.deepEqual(names,
            ['NB_ShieldRipple.tga', 'NB_ShieldWave.tga', 'shield_color.tga']);
    });

    it('leaves numeric parameters out, however texture-like the name', () => {
        const names = collectTextureNames([{
            alamoShader: 'MeshBumpColorize.fx',
            'param:Colorization': '1,1,1,0',
            'param:Shininess': '32',
        }]);

        assert.deepEqual(names, []);
    });

    it('still skips a sub-mesh that is never drawn', () => {
        const names = collectTextureNames([{
            alamoShader: 'MeshShadowVolume.fx',
            'param:BaseTexture': 'never_drawn.tga',
        }]);

        assert.deepEqual(names, []);
    });
});

describe('debugColour', () => {
    // Collision hulls and shadow volumes are drawn in a flat authored colour. It is NOT hardcoded:
    // each is a `ColorSwatch` parameter on the sub-mesh, defaulting to blue at half alpha for
    // collision and opaque cyan for shadow volumes. Measured across the exported models, every one
    // writes the value explicitly and every one matches its shader's default.
    it('reads a collision hull`s colour', () => {
        assert.deepEqual(
            debugColour({ alamoShader: 'MeshCollision.fx', 'param:Color': '0,0,1,0.5' }),
            { r: 0, g: 0, b: 1, a: 0.5 });
    });

    it('reads a shadow volume`s, which uses a different parameter name', () => {
        assert.deepEqual(
            debugColour({ alamoShader: 'MeshShadowVolume.fx', 'param:DebugColor': '0,1,1,1' }),
            { r: 0, g: 1, b: 1, a: 1 });
    });

    it('assumes opaque when the parameter carries only three channels', () => {
        assert.deepEqual(
            debugColour({ 'param:Color': '1,0,0' }), { r: 1, g: 0, b: 0, a: 1 });
    });

    it('has nothing to say about a mesh that declares neither', () => {
        assert.equal(debugColour({ alamoShader: 'MeshBumpColorize.fx' }), null);
    });
});

describe('shadow volumes take over from the shadow map', () => {
    it('stops visible geometry casting a mapped shadow once the model authors a volume', () => {
        // The engine casts from the authored volume, never from the render mesh. Leaving the shadow
        // map casting as well would draw the same shadow twice, from two different silhouettes.
        assert.equal(castsShadowMap({ hasVolumes: true, hidden: false, blend: 'opaque' }), false);
    });

    it('keeps casting from the hull on the 63% of models that author no volume', () => {
        assert.equal(castsShadowMap({ hasVolumes: false, hidden: false, blend: 'opaque' }), true);
    });

    it('never casts from geometry the engine does not draw', () => {
        assert.equal(castsShadowMap({ hasVolumes: false, hidden: true, blend: 'opaque' }), false);
    });

    it('never casts from an additive or alpha pass', () => {
        // A glow has no silhouette worth casting, and an alpha card would cast its whole quad.
        assert.equal(castsShadowMap({ hasVolumes: false, hidden: false, blend: 'additive' }), false);
    });
});

describe('isVisibleAtLevel', () => {
    it('asks ONLY about the damage and detail level', () => {
        // `alamoHidden` is a different question - "does the model draw this at all" - and it is
        // asked separately, by the row chain's `inFile` link. Bundling the two here meant an
        // ability that talked the model into drawing its shield was still vetoed by the level link
        // reading the same flag. One flag, two gates, and overriding one did nothing.
        assert.equal(isVisibleAtLevel({ alamoHidden: true }, 0, 0), true);
    });

    it('still gates on ALT and LOD', () => {
        assert.equal(isVisibleAtLevel({ alamoAlt: 1 }, 0, 0), false);
        assert.equal(isVisibleAtLevel({ alamoAlt: 1 }, 1, 0), true);
        assert.equal(isVisibleAtLevel({ alamoLod: 2 }, 0, 0), false);
        assert.equal(isVisibleAtLevel({ alamoLod: 2 }, 0, 2), true);
    });

    it('is what isVisibleAt asks once the hidden flag has passed', () => {
        // The two stay in step by construction: isVisibleAt is the hidden check plus this.
        assert.equal(isVisibleAt({ alamoAlt: 1 }, 1, 0), isVisibleAtLevel({ alamoAlt: 1 }, 1, 0));
        assert.equal(isVisibleAt({ alamoLod: 3 }, 0, 0), isVisibleAtLevel({ alamoLod: 3 }, 0, 0));
    });
});

describe('isShieldMesh', () => {
    it('takes the mesh the author NAMED shield, whatever shader it is on', () => {
        // The user's rule: the name decides. Nothing in the data declares which mesh a shield
        // ability reveals, and the author naming it `shield` is the clearest word there is.
        assert.equal(isShieldMesh({ alamoMesh: 'shield', alamoShader: 'MeshShield.fx' }), true);
        assert.equal(isShieldMesh({ alamoMesh: 'Shield_01', alamoShader: 'MeshBumpSpecular.fx' }), true);
    });

    it('leaves ENGINE GLOW on the same shader alone', () => {
        // MEASURED on `Rv_moncalcruiser.alo`: three meshes carry MeshShield.fx - `shield`,
        // `engines_big` and `engines_small`. Deciding on the shader lit the engines whenever DEFEND
        // was switched on, which is not what the ability does.
        assert.equal(
            isShieldMesh({ alamoMesh: 'engines_big', alamoShader: 'MeshShield.fx' }), false);
        assert.equal(
            isShieldMesh({ alamoMesh: 'engines_small', alamoShader: 'MeshShield.fx' }), false);
    });

    it('does not take a mesh that merely CONTAINS the word', () => {
        // `HP_Shield_Generator` is a hardpoint housing - hull geometry that must not vanish and
        // reappear with an ability. The name has to START with it.
        assert.equal(isShieldMesh({ alamoMesh: 'HP_Shield_Generator' }), false);
        assert.equal(isShieldMesh({ alamoMesh: 'deflector_shield' }), false);
    });

    it('says no with no name at all', () => {
        assert.equal(isShieldMesh({}), false);
    });
});

describe('isStealthMesh', () => {
    // Measured over all 3340 shipped models: nine carry one, and every one is named `stealth` or
    // `stealth_LOD<n>` in one case or another - `Ev_tie_phantom`, `Ri_skywalker`, `Uv_vengeance`,
    // `Ui_tyber`, `Ui_tyber_in_jail`, `Ui_urai_fen`, `Uv_f9tztransport`,
    // `Uv_kraytclassdestroyer_tyber`, `W_mousedroid`.
    it('takes the shell by the name the models use', () => {
        assert.equal(isStealthMesh({ alamoMesh: 'stealth' }), true);
        assert.equal(isStealthMesh({ alamoMesh: 'Stealth' }), true);
    });

    it('takes the LOD variants, which three of the nine ship', () => {
        // Tyber and Urai Fen carry `Stealth_LOD0/1/2`, so the suffix has to pass the name test and
        // then be gated by the ordinary LOD rules like any other mesh.
        assert.equal(isStealthMesh({ alamoMesh: 'Stealth_LOD0' }), true);
        assert.equal(isStealthMesh({ alamoMesh: 'stealth_LOD2' }), true);
    });

    it('leaves a CLOAK alone, which is a garment and not this', () => {
        // The trap a looser test falls into. Vader, Palpatine, Obi-Wan, Yoda and the sand people
        // all carry a `cloak` mesh, and it is clothing - hiding the hull behind it would strip
        // five heroes the moment anything went near the stealth rules.
        for (const name of ['Cloak', 'EI_Palpatine_cloak', 'RI_ObiCloak', 'NI_SandPeople_CLoak',
            'Shadow_Cloak']) {
            assert.equal(isStealthMesh({ alamoMesh: name }), false, name);
        }
    });

    it('needs the whole word, not a prefix of one', () => {
        assert.equal(isStealthMesh({ alamoMesh: 'stealthy_bits' }), false);
        assert.equal(isStealthMesh({ alamoMesh: 'basemesh' }), false);
        assert.equal(isStealthMesh({}), false);
    });
});

describe('shieldMeshOffShader', () => {
    it('flags a shield mesh that is not on a shield shader', () => {
        // Not an error - the name decides and the author may mean it - but a bubble drawn with a
        // hull shader reads as solid geometry rather than a field, which is worth saying once.
        assert.equal(
            shieldMeshOffShader({ alamoMesh: 'shield', alamoShader: 'MeshBumpSpecular.fx' }), true);
    });

    it('says nothing about a shield mesh that IS on one', () => {
        assert.equal(
            shieldMeshOffShader({ alamoMesh: 'shield', alamoShader: 'MeshShield.fx' }), false);
    });

    it('says nothing about a mesh that is not a shield at all', () => {
        assert.equal(
            shieldMeshOffShader({ alamoMesh: 'engines_big', alamoShader: 'MeshShield.fx' }), false);
    });
});

// The base shaders are Petroglyph's and are NEVER shipped with the extension, so unless the reader
// has fetched their own copy every mesh keeps its archetype - and the archetype is picked by NAME
// token. "tree" contains none of additive/shield/alpha, so `Tree.fx` fell through to plain opaque
// with no cutoff and its foliage drew as SOLID GREEN SLABS. That is the reported missing leaves.
//
// Measured from the shipped effects: exactly three enable alpha testing - `Tree.fx` at 0x80,
// `Grass.fx` at 0x08, and `BlobStencilMasked.fx` at 0x80 (used by no shipped model).
describe('resolveMaterial: the alpha-TESTED effects', () => {
    it('gives Tree.fx the cutoff its own render state declares', () => {
        const spec = resolveMaterial({ alamoShader: 'Tree.fx' });

        assert.ok(spec.alphaTest !== null && Math.abs(spec.alphaTest - 0x80 / 255) < 1e-6,
            `${spec.alphaTest}`);
    });

    // Alpha TESTED, not blended: `AlphaBlendEnable = FALSE` and `ZWriteEnable = TRUE`. Reading the
    // effect's `_ALAMO_RENDER_PHASE = "Transparent"` as a blend would sort the foliage against the
    // hull and drop its depth writes, which is a different wrong picture.
    it('leaves Tree.fx opaque and writing depth', () => {
        const spec = resolveMaterial({ alamoShader: 'Tree.fx' });

        assert.equal(spec.blend, 'opaque');
        assert.equal(spec.depthWrite, true);
    });

    it('gives Grass.fx its own much lower cutoff', () => {
        const spec = resolveMaterial({ alamoShader: 'Grass.fx' });

        assert.ok(spec.alphaTest !== null && Math.abs(spec.alphaTest - 0x08 / 255) < 1e-6,
            `${spec.alphaTest}`);
    });

    it('asks for no cutoff on an effect that declares none', () => {
        assert.equal(resolveMaterial({ alamoShader: 'MeshBumpColorize.fx' }).alphaTest, null);
        assert.equal(resolveMaterial({ alamoShader: 'MeshAlpha.fx' }).alphaTest, null);
    });
});

// `None` is the model format's own way of saying a texture slot is EMPTY, and it is the only
// placeholder the shipped trees use: 184 occurrences across 84 models - NormalTexture 171,
// CloudNormalTexture 6, GlossTexture 5 and BaseTexture 2. Read as a file name it becomes a request
// for a texture called "None", which can never resolve; it counts against the loading cover, shows
// up as a problem, and on the two BaseTexture cases paints the missing-texture marker.
describe('textureParam: the None placeholder', () => {
    it('reads a real texture name', () => {
        assert.equal(
            textureParam({ 'param:BaseTexture': 'W_Tree_Alien00.TGA' }, 'BaseTexture'),
            'W_Tree_Alien00.TGA');
    });

    it('treats None as an empty slot', () => {
        assert.equal(textureParam({ 'param:NormalTexture': 'None' }, 'NormalTexture'), undefined);
    });

    it('does not care how None is spelled', () => {
        // The format is inconsistent about casing everywhere else it is read, so this is too.
        assert.equal(textureParam({ 'param:NormalTexture': 'none' }, 'NormalTexture'), undefined);
        assert.equal(textureParam({ 'param:NormalTexture': ' NONE ' }, 'NormalTexture'), undefined);
    });

    // A real file that merely STARTS with those letters is not a placeholder. Only the bare word is.
    it('keeps a real texture whose name begins with none', () => {
        assert.equal(
            textureParam({ 'param:BaseTexture': 'none_at_all.tga' }, 'BaseTexture'),
            'none_at_all.tga');
    });

    // The REQUEST list is a second reader of the same parameters, with its own copy of the
    // emptiness rule - so fixing only `textureParam` left the host still being asked for "None".
    it('never asks the host for a texture called None', () => {
        const names = collectTextureNames([{
            alamoShader: 'Tree.fx',
            'param:BaseTexture': 'W_Tree_Alien00.TGA',
            'param:NormalTexture': 'None',
        }]);

        assert.deepEqual(names, ['W_Tree_Alien00.TGA']);
    });

    it('binds no normal map when the slot says None', () => {
        const spec = resolveMaterial({
            alamoShader: 'MeshBumpColorize.fx',
            'param:BaseTexture': 'hull.tga',
            'param:NormalTexture': 'None',
        });

        assert.equal(spec.textures.base, 'hull.tga');
        assert.equal(spec.textures.normal, undefined);
    });
});
