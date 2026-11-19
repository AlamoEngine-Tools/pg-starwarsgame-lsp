// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The viewport's materials: which material each mesh is drawn with (the translated effect, the archetype or
// a debug view), the shader states, effects and textures bound to them, the team tint, wireframe, and the
// per-frame Alamo uniforms. It reads the meshes, the camera and the lighting frame; nothing else.

import { type TranslatedEffect } from "./fx/effect";
import { type FxMaterialState } from "./fx/renderState";
import * as THREE from "three";
import { type NormalisedColour } from "./colour";
import {
    type MaterialExtras,
    shieldMeshOffShader,
    resolveMaterial,
    castsShadowMap,
    type MaterialSpec,
    debugColour,
} from "./materials";
import { AlamoMaterial } from "./alamoMaterial";
import { meshDrawn } from "./meshVisibility";
import * as l10n from "@vscode/l10n";
import { alamoNodeName } from "./skeleton";
import {
    applyBlend,
    applyDepth,
    applyColourScale,
    applyBaseMapColourSpace,
} from "./materialState";
import { type ViewportContext } from "./viewportContext";

/** The flat grey an untinted mesh draws in, until the real shaders land. */
const UNTINTED = 0xb8b8b8;

/**
 * How solid a collision hull or shadow volume draws.
 *
 * Both are seen THROUGH, and for a collision hull that is the entire reason to look at one: the
 * question being asked is whether the hull encloses the model, which you can only answer if the
 * geometry inside it is visible and anything poking out of it reads immediately.
 *
 * Lower than it looks, because the value COMPOUNDS. A rancor carries three shadow volumes and two
 * collision hulls, and at the effects' own alpha - the ShadowVolume ones ask for FULL - the five of
 * them stacked into a solid block with the model nowhere in it.
 */
const DEBUG_HULL_OPACITY = 0.3;

export class MaterialBinder {
    /** Effects that translated, by lower-cased shader name. */
    readonly translated = new Map<string, TranslatedEffect>();
    /** Declared render state per shader, so a material rebuilt later is not left on the defaults. */
    readonly shaderStates = new Map<string, FxMaterialState>();
    /** Draw with a translated shader wherever one is available. */
    translatedShaders = true;
    private wireframe = false;
    /**
     * The team tint, or null for the model's own colours.
     *
     * Kept here rather than applied and forgotten, because materials are rebuilt whenever a part
     * loads - a turret arriving after the faction was picked has to come in already tinted.
     */
    colorization: NormalisedColour | null = null;

    constructor(private readonly context: ViewportContext) {}

    /** Puts every declared render state and the team tint onto the materials built since they were set. */
    applyStoredState(): void {
        for (const [shader, state] of this.shaderStates) {
            this.applyStoredShaderState(shader, state);
        }

        if (this.colorization !== null) {
            this.setColorization(this.colorization);
        }
    }

    /**
     * Applies a shader's declared render state to every material using it.
     *
     * This is the fidelity that needs no shader translation: the effects spell their blend, depth
     * and cull state out declaratively. A `custom` blend is left alone deliberately - the archetype
     * guess is better than a wrong one.
     */
    applyShaderState(shaderName: string, state: FxMaterialState): void {
        this.shaderStates.set(shaderName.toLowerCase(), state);
        this.applyStoredShaderState(shaderName.toLowerCase(), state);
        this.applyParticleEffects();
    }

    /** Every distinct shader name the loaded geometry names, so each is fetched once. */
    shaderNames(): string[] {
        const names = new Set<string>();

        this.context.modelRoot.traverse((node) => {
            if (!(node instanceof THREE.Mesh)) {
                return;
            }

            const shader =
                (node.userData.alamo as MaterialExtras | undefined)
                    ?.alamoShader ?? "";
            if (shader !== "") {
                names.add(shader);
            }
        });

        // The particle systems' effects too: an emitter is drawn through the engine's own
        // `Prim*.fx` for its render mode, fetched and translated like any mesh effect.
        for (const entry of this.context.particles.particleSystems.values()) {
            for (const name of entry.instance.effectNames()) {
                names.add(name);
            }
        }

        return [...names].sort();
    }

    /**
     * Hands every particle system the translated effects in hand, or takes them away.
     *
     * Rerun whenever an effect or a pass state arrives and when the translation toggle moves: a
     * system built before its effect came back has to pick it up the same as one built after.
     */
    private applyParticleEffects(): void {
        for (const entry of this.context.particles.particleSystems.values()) {
            entry.instance.applyEffects(
                this.translated,
                this.shaderStates,
                this.translatedShaders,
            );
        }
    }

    /** Sets the team tint, or clears it back to the model's own colours. */
    setColorization(colour: NormalisedColour | null): void {
        this.colorization = colour;

        this.context.assembly.forEachDrawnMesh((node) => {
            const material = node.material;

            if (material instanceof AlamoMaterial) {
                // The effect masks the tint itself; it only needs the colour.
                material.setColorization(colour);
            } else if (
                material instanceof THREE.MeshStandardMaterial ||
                material instanceof THREE.MeshBasicMaterial
            ) {
                // Basic as well as standard: an FC_ mesh on an additive shader is unlit and still
                // takes the team colour, so checking only for standard left it untinted.
                this.tint(material);
            }
        });
    }

    /** Which distinct sub-mesh names take the team colour, for the dock to report. */
    colorizedMeshes(): string[] {
        const names = new Set<string>();

        this.context.modelRoot.traverse((node) => {
            if (!(node instanceof THREE.Mesh)) {
                return;
            }

            const data = node.material as {
                userData?: { spec?: { colorize?: boolean } };
            };
            const extras = node.userData.alamo as MaterialExtras | undefined;

            if (
                data.userData?.spec?.colorize === true &&
                extras?.alamoMesh !== undefined
            ) {
                names.add(extras.alamoMesh);
            }
        });

        return [...names].sort();
    }

    /**
     * Draws with a translated effect instead of its archetype, wherever that effect is used.
     *
     * Kept alongside the archetype rather than replacing it: a mod-authored `.fx` will never
     * translate, the eleven fixed-function effects have no shader at all, and the archetype is what
     * both fall back to. `setTranslatedShaders(false)` puts everything back on that path, which is
     * the only way to see what the translation is actually changing.
     */
    applyTranslatedEffect(shaderName: string, effect: TranslatedEffect): void {
        this.translated.set(shaderName.toLowerCase(), effect);

        if (this.translatedShaders) {
            this.rebuildMaterials();
        }
    }

    /** Whether to draw with translated shaders where one is available. */
    setTranslatedShaders(enabled: boolean): void {
        if (this.translatedShaders === enabled) {
            return;
        }

        this.translatedShaders = enabled;
        this.rebuildMaterials();
    }

    /**
     * How many loaded sub-meshes and emitters a translated effect actually reaches, out of how many
     * there are. The emitters count because a particle file has no sub-meshes at all.
     */
    translatedCoverage(): { translated: number; total: number } {
        let translated = 0;
        let total = 0;

        this.context.assembly.forEachPartMesh((mesh) => {
            if (!meshDrawn(mesh)) {
                return;
            }

            total++;
            if (mesh.material instanceof AlamoMaterial) {
                translated++;
            }
        });

        for (const entry of this.context.particles.particleSystems.values()) {
            const coverage = entry.instance.effectCoverage();
            translated += coverage.translated;
            total += coverage.total;
        }

        return { translated, total };
    }

    /**
     * Samplers on translated materials that are reading nothing, in words.
     *
     * A sampler with no texture reads black, and the shader then draws something plausible and
     * wrong - the sort of thing that looks like a translation bug and is actually a missing file.
     */
    shaderTextureProblems(): string[] {
        const problems = new Set<string>();

        this.context.assembly.forEachPartMesh((mesh) => {
            if (!(mesh.material instanceof AlamoMaterial)) {
                return;
            }

            const shader = (mesh.userData.alamo as MaterialExtras | undefined)
                ?.alamoShader;

            for (const { sampler, file } of mesh.material.unboundSamplers()) {
                problems.add(
                    shader === undefined
                        ? l10n.t(
                              "A shader: Sampler '{0}' is still waiting for '{1}'.",
                              sampler,
                              file,
                          )
                        : l10n.t(
                              "{0}: Sampler '{1}' is still waiting for '{2}'.",
                              shader,
                              sampler,
                              file,
                          ),
                );
            }
            for (const sampler of mesh.material.unnamedSamplers) {
                problems.add(
                    shader === undefined
                        ? l10n.t(
                              "A shader: Sampler '{0}' has no texture on this sub-mesh.",
                              sampler,
                          )
                        : l10n.t(
                              "{0}: Sampler '{1}' has no texture on this sub-mesh.",
                              shader,
                              sampler,
                          ),
                );
            }
        });

        return [...problems];
    }

    /** Applies a decoded texture wherever a material asked for it by name. */
    setTexture(name: string, texture: THREE.Texture): void {
        // The store disposes the texture this one replaces, if it replaces one.
        this.context.textures.set(name, texture);
        this.bindTextureIn(this.context.modelRoot, name, texture);
    }

    /**
     * Binds every texture already decoded into a subtree that has only just been added.
     *
     * `setTexture` binds at the moment the texture ARRIVES, so geometry added afterwards gets
     * nothing - and asking again does not help, because a name is asked for once per scene and a
     * repeat is never posted. Wreckage is the case that exposed this: a
     * breakoff prop is usually a piece of the hull it fell off, so its textures are always ones
     * already in hand, and it dropped as a black shape every time.
     */
    bindLoadedTextures(root: THREE.Object3D): void {
        for (const [name, texture] of this.context.textures.entries()) {
            this.bindTextureIn(root, name, texture);
        }
    }

    /**
     * Shield meshes the model names but does not draw with a shield shader.
     *
     * Reported to the reader rather than corrected: the NAME is what decides which mesh a shield
     * ability reveals, so the author's word stands - but a bubble on a hull shader will read as
     * solid geometry rather than a field, and that is worth saying once.
     */
    shieldMeshesOffShader(): string[] {
        const names = new Set<string>();

        this.context.assembly.forEachPartMesh((mesh) => {
            const extras = (mesh.userData.alamo ?? {}) as MaterialExtras;

            if (shieldMeshOffShader(extras)) {
                names.add(extras.alamoMesh ?? alamoNodeName(mesh));
            }
        });

        return [...names];
    }

    /**
     * Draws every part mesh as its edges.
     *
     * Reaches the translated effects as well as the archetype materials: `wireframe` is declared on
     * `THREE.Material`, and a `RawShaderMaterial` inherits it through `ShaderMaterial`, so the flag
     * is honoured whichever renderer the reader has chosen. Particle systems are pruned by
     * `forEachPartMesh` and stay as they are - a wireframe billboard is a pair of triangles and
     * says nothing about the model.
     */
    setWireframe(on: boolean): void {
        this.wireframe = on;
        this.applyWireframe();
    }

    /**
     * The Alamo extras of every loaded material.
     *
     * Read back off the scene rather than tracked alongside it, so the texture request list can
     * never fall out of step with what was actually loaded.
     */
    materialExtras(): MaterialExtras[] {
        const all: MaterialExtras[] = [];

        this.context.modelRoot.traverse((node) => {
            if (
                node instanceof THREE.Mesh &&
                node.userData.alamo !== undefined
            ) {
                all.push(node.userData.alamo as MaterialExtras);
            }
        });

        return all;
    }

    /**
     * Replaces a loaded mesh's material with what the seam says to draw.
     *
     * The glTF material is only a carrier - the exporter puts the Alamo shader name and its parameters
     * in `extras` precisely because glTF cannot express them.
     */
    applyMaterial(mesh: THREE.Mesh): void {
        // On the FIRST pass the glTF material is the carrier. After that this mesh's material is one
        // we made, whose userData is `{ spec, extras }` - reading extras off it again would find no
        // shader name at all, which silently drops every texture and every translated effect.
        const source = Array.isArray(mesh.material)
            ? mesh.material[0]
            : mesh.material;
        const extras = (mesh.userData.alamo ??
            source?.userData ??
            {}) as MaterialExtras;
        const spec = resolveMaterial(extras);

        mesh.userData.alamo = extras;

        // Nothing about WHETHER the mesh is drawn, or whether a shadow volume casts: both belong to
        // the row chain and a passive subject's own sweep. Setting visibility from the material spec
        // here re-showed every mesh the levels had hidden each time the shader toggle rebuilt the
        // materials, collision and shadow hulls included.

        // Only SOLID geometry casts. A collision hull and a shadow volume are overlays and would
        // throw a second silhouette over the model; an additive glow or an alpha-blended girder has
        // no opaque body to cast from, and a shadow map has no way to express a partial one - an
        // engine flare casting a hard black shadow is worse than it casting none.
        // Once a model authors a shadow volume, the volume IS the shadow - the hull stops casting
        // into the map, exactly as the engine does it. Recomputed for every mesh whenever a part
        // arrives, because a volume can turn up after the geometry that it belongs to.
        mesh.castShadow = castsShadowMap({
            hasVolumes: this.context.environment.stencilShadowing,
            hidden: spec.hidden,
            blend: spec.blend,
        });
        mesh.receiveShadow = !spec.hidden;

        // A collision hull or a shadow volume draws NOTHING in the engine - the shadow techniques
        // literally set `ColorWriteEnable = 0` and write only stencil. But the reason to tick one
        // on in a preview is to LOOK at it, so it gets the debug colour its own effect declares:
        // `MeshCollision.fx` ships `Color = {0, 0, 1, 0.5}` and both ShadowVolume effects ship
        // `DebugColor = {0, 1, 1, 1}`. Left on the glTF material they came in on, both drew as
        // flat white, which is the one thing they are definitely not.
        if (spec.hidden) {
            mesh.material = this.debugMaterial(extras, spec);
            return;
        }

        // The effect's own shader when there is one and it is switched on, the archetype otherwise.
        const effect = this.translated.get(
            (extras.alamoShader ?? "").toLowerCase(),
        );
        if (this.translatedShaders && effect !== undefined) {
            const translated = new AlamoMaterial(effect, extras);

            // A rebuild makes new materials, so the current tint has to be put back on them.
            if (spec.colorize) {
                translated.setColorization(this.colorization);
            }

            translated.userData = { spec, extras };
            mesh.material = translated;
            return;
        }

        const shared = {
            transparent: spec.blend !== "opaque",
            depthWrite: spec.depthWrite,
            side: THREE.FrontSide,

            // The cutoff the effect declares, carried by the archetype because the shader itself is
            // usually not reachable - the base shaders are Petroglyph's and are never shipped. Zero
            // discards nothing, which is what drew `Tree.fx` foliage as solid slabs. Overridden by
            // `applyShaderState` for anyone who does have the effect in hand.
            alphaTest: spec.alphaTest ?? 0,
        };

        // Unlit for the additive family, and it has to be a different MATERIAL rather than a
        // brighter light: a standard material's dielectric specular is not multiplied by the base
        // colour, so a black texel still reflects about 4% of the key light. Under an additive
        // blend that 4% is the muzzle flash's whole quad showing as a grey rectangle around the
        // flare. `Additive.fxh` is `texel * constant`, so basic is not an approximation here - it
        // is what the effect does.
        const material = spec.lit
            ? new THREE.MeshStandardMaterial({
                  ...shared,
                  color: UNTINTED,
                  metalness: 0,
                  roughness: 0.85,
              })
            : // White, not UNTINTED: nothing is going to light this, so any grey here just dims the
              // texture the effect meant to show at full strength.
              new THREE.MeshBasicMaterial({ ...shared, color: 0xffffff });

        // The archetype's own blend and depth, through the single place that knows how each one is
        // expressed in three. An additive ARCHETYPE means the engine's ONE, ONE - the same thing
        // the effect would have declared - so the two routes to a material cannot disagree, and a
        // mesh does not shift when its effect finally arrives.
        applyBlend(material, spec.blend);
        applyDepth(material, {
            depthTest: true,
            depthWrite: spec.depthWrite,
            depthFunc: "lessEqual",
        });

        material.userData = { spec, extras };
        this.tint(material);
        mesh.material = material;

        // The effect's own declared blend, depth and cull state, if we have read it. Applied here
        // and not only when the shader arrives, because every material rebuild - a shader toggle, a
        // faction change - throws the previous one away and would otherwise silently fall back to
        // the archetype's guess for the rest of the session.
        const state = this.shaderStates.get(
            (extras.alamoShader ?? "").toLowerCase(),
        );
        if (state !== undefined) {
            this.applyStoredShaderState(
                (extras.alamoShader ?? "").toLowerCase(),
                state,
            );
        }
    }

    /**
     * How a non-visual hull is drawn when the reader asks to see it.
     *
     * Neither of these is drawn by the engine at all - the shadow techniques set
     * `ColorWriteEnable = 0` and write only stencil - so there is no "correct" appearance to copy,
     * only a useful one. The COLOUR is still the effect's own: `MeshCollision.fx` ships
     * `Color = {0, 0, 1, 0.5}` and both ShadowVolume effects ship `DebugColor = {0, 1, 1, 1}`, and
     * a mod is free to change either.
     *
     * The two differ in what they ARE, so they differ in how they draw:
     *
     * - a COLLISION hull is a closed solid standing in for the model, and nobody switches one on by
     *   accident, so it draws solid and occludes - which is what makes its shape readable;
     * - a SHADOW VOLUME is an extruded silhouette whose interior is the point, so it stays
     *   translucent and writes no depth.
     */
    private debugMaterial(
        extras: MaterialExtras,
        spec: MaterialSpec,
    ): THREE.Material {
        // The effect's own declared colour where there is one, and the colour that effect DEFAULTS
        // to otherwise. A hull authored on `alDefault.fx` - 174 of them across the shipped models -
        // declares no swatch at all, so the fallback has to come from what KIND of hull this is, or
        // the Nebulon-B's `COLLISION` draws in the shadow volume's cyan.
        const volume = spec.archetype === "shadow-volume";
        const colour =
            debugColour(extras) ??
            (volume ? { r: 0, g: 1, b: 1, a: 1 } : { r: 0, g: 0, b: 1, a: 1 });

        const material = new THREE.MeshBasicMaterial({
            color: new THREE.Color(colour.r, colour.g, colour.b),
            opacity: Math.min(colour.a, DEBUG_HULL_OPACITY),
            transparent: true,
            // No depth write, so the hull layers OVER the model rather than carving it out - the
            // model has to stay visible through it for the enclosure to be readable.
            depthWrite: false,
            // Front faces only. Double-sided doubles the translucent layers to see through, and a
            // closed hull's back faces add nothing to reading its shape.
            side: THREE.FrontSide,
        });

        material.userData = { spec, extras };
        return material;
    }

    /**
     * Applies the team colour to one material, if that material takes it.
     *
     * Which materials do is the seam's business: a `*Colorize` shader, or an `FC_`-prefixed mesh,
     * which takes the colour whatever its shader says.
     *
     * PER PIXEL, weighted by the base texture's ALPHA - not a flat tint over the whole sub-mesh.
     * That is what the shipped shaders do, `lerp(base.rgb, Colorization * base.rgb, base.a)`, and
     * it is the difference between an Imperial hull with coloured markings and an Imperial hull
     * painted entirely green. `material.color` therefore stays white; the tint is injected into the
     * standard material's own fragment shader, because nothing three offers expresses "multiply by
     * this colour only where the map's alpha says so".
     */
    private tint(
        material: THREE.MeshStandardMaterial | THREE.MeshBasicMaterial,
    ): void {
        const spec = (material.userData as { spec?: { colorize?: boolean } })
            .spec;
        if (spec?.colorize !== true) {
            return;
        }

        material.color.setHex(0xffffff);
        this.installColorization(material);

        const uniform = (
            material.userData as { colorization?: { value: THREE.Color } }
        ).colorization;
        if (uniform === undefined) {
            return;
        }

        // White is the identity for a multiply, so an untinted mesh keeps its texture exactly.
        if (this.colorization === null) {
            uniform.value.setRGB(1, 1, 1);
            return;
        }

        uniform.value.setRGB(
            this.colorization.r,
            this.colorization.g,
            this.colorization.b,
        );
    }

    /**
     * Teaches one standard material to tint by the base map's alpha.
     *
     * `sampledDiffuseColor` is declared by three's own `map_fragment` chunk and is still in scope
     * after it, so the mask is the texture's real alpha rather than anything already multiplied
     * into `diffuseColor`.
     */
    private installColorization(
        material: THREE.MeshStandardMaterial | THREE.MeshBasicMaterial,
    ): void {
        const data = material.userData as {
            colorization?: { value: THREE.Color };
        };
        if (data.colorization !== undefined) {
            return;
        }

        const colorization = { value: new THREE.Color(1, 1, 1) };
        data.colorization = colorization;

        material.onBeforeCompile = (shader) => {
            shader.uniforms.aetColorization = colorization;

            shader.fragmentShader = shader.fragmentShader
                .replace(
                    "void main() {",
                    "uniform vec3 aetColorization;\nvoid main() {",
                )
                .replace(
                    "#include <map_fragment>",
                    `#include <map_fragment>
#ifdef USE_MAP
    diffuseColor.rgb = mix(
        diffuseColor.rgb, aetColorization * diffuseColor.rgb, sampledDiffuseColor.a);
#endif`,
                );
        };

        // three caches programs by this key, so two materials differing only in the injection would
        // otherwise share one compiled shader.
        material.customProgramCacheKey = () => "aet-colorize";
    }

    /**
     * Puts one shader's render state onto every material currently using it.
     *
     * Both kinds: a translated `AlamoMaterial` needs the blend and depth state just as much as the
     * archetype does, and skipping it drew meshes that should be alpha-blended with depth writes on
     * - which is how an inner hull ends up occluding the model around it.
     */
    applyStoredShaderState(wanted: string, state: FxMaterialState): void {
        this.context.assembly.forEachDrawnMesh((node) => {
            const material = node.material;
            const extras = node.userData.alamo as MaterialExtras | undefined;

            if (
                Array.isArray(material) ||
                !(material instanceof THREE.Material) ||
                (extras?.alamoShader ?? "").toLowerCase() !== wanted
            ) {
                return;
            }

            // A collision hull or shadow volume is not drawn by the engine at all, so the state its
            // effect declares describes a pass we are not running - `MeshCollision.fx` asks for
            // opaque depth writes, which turned the translucent blue debug hull back into a solid
            // box over the model. The debug material keeps its own state.
            if (resolveMaterial(extras ?? {}).hidden) {
                return;
            }

            applyDepth(material, state);
            material.side =
                state.cull === "none"
                    ? THREE.DoubleSide
                    : state.cull === "front"
                      ? THREE.BackSide
                      : THREE.FrontSide;

            // Two different mechanisms for the same declaration. An archetype material is one of
            // three's own, which reads `alphaTest` and injects the discard itself; a translated
            // effect is a RawShaderMaterial, which gets no such chunk and needs the threshold as a
            // uniform. Missing the second is what drew `Tree.fx`'s palm fronds - a leaf texture on
            // a plain rectangle - as solid green cards.
            if (material instanceof AlamoMaterial) {
                material.setAlphaTest(state.alphaTest);
            } else {
                // Cleared rather than left, so a material that inherited a cutoff does not keep
                // one the effect never asked for.
                material.alphaTest = state.alphaTest ?? 0;
            }

            applyBlend(material, state.blend);

            // The fixed-function stage scale. Only the archetype path needs it: a translated effect
            // is running the effect's own HLSL, which does its own doubling, and `colourScale` is 1
            // for any pass carrying a shader precisely so this cannot apply twice.
            if (!(material instanceof AlamoMaterial)) {
                applyColourScale(material, state.colourScale);
            }

            material.needsUpdate = true;
        });
    }

    /**
     * Feeds every translated material what the engine would recompute this frame.
     *
     * Per mesh rather than per scene, because half the contract is object-relative: WORLD and
     * WORLDVIEWPROJECTION differ for a turret attached on a hull, and a shader working in object
     * space wants the eye and the lights transformed into ITS space, not the hull's.
     */
    updateAlamoUniforms(): void {
        const materials: { mesh: THREE.Mesh; material: AlamoMaterial }[] = [];

        this.context.modelRoot.traverse((node) => {
            if (
                node instanceof THREE.Mesh &&
                node.material instanceof AlamoMaterial &&
                node.visible
            ) {
                materials.push({ mesh: node, material: node.material });
            }
        });

        if (materials.length === 0) {
            return;
        }

        this.context.camera.updateMatrixWorld();

        const view = this.context.camera.matrixWorldInverse;
        const projection = this.context.camera.projectionMatrix;
        const viewProjection = new THREE.Matrix4().multiplyMatrices(
            projection,
            view,
        );
        const eyeWorld = this.context.camera.getWorldPosition(
            new THREE.Vector3(),
        );

        for (const { mesh, material } of materials) {
            material.updateFrame(
                this.context.environment.frameFor(
                    mesh,
                    view,
                    projection,
                    viewProjection,
                    eyeWorld,
                ),
            );
        }
    }

    // ── animation ─────────────────────────────────────────────────────────────

    /** Rebuilds every mesh's material for the current translated/archetype choice. */
    private rebuildMaterials(): void {
        this.context.assembly.forEachDrawnMesh((mesh) =>
            this.applyMaterial(mesh),
        );

        // Same reason as after a part loads: these are different material objects, and a flag set
        // on the ones they replace is gone.
        this.applyWireframe();

        // New material objects, so everything already known has to be applied to them again.
        for (const [shader, state] of this.shaderStates) {
            this.applyStoredShaderState(shader, state);
        }

        // Materials are new objects, so everything already decoded has to be handed over again.
        for (const [name, texture] of this.context.textures.entries()) {
            this.setTexture(name, texture);
        }
        this.applyParticleEffects();
    }

    /**
     * Whether any mesh in the MODEL draws this texture with one of three's own materials.
     *
     * Asked of `modelRoot` rather than of whatever subtree is being bound: the texture is shared
     * across the whole scene, so a newly added prop must not flip an answer the rest of the model
     * has already been drawn with.
     */
    private archetypeWants(wanted: string): boolean {
        let found = false;

        this.context.modelRoot.traverse((node) => {
            if (
                found ||
                !(node instanceof THREE.Mesh) ||
                node.material instanceof AlamoMaterial
            ) {
                return;
            }

            const spec = (node.material as THREE.Material).userData?.spec as
                { textures?: { base?: string } } | undefined;

            if (spec?.textures?.base?.toLowerCase() === wanted) {
                found = true;
            }
        });

        return found;
    }

    private bindTextureIn(
        root: THREE.Object3D,
        name: string,
        texture: THREE.Texture,
    ): void {
        const wanted = name.toLowerCase();

        // ONE answer for the texture, taken from the whole model before anything is bound. The
        // cache holds a single THREE.Texture per name and shares it, so the decode is a property of
        // the TEXTURE and cannot be decided per material - and it cannot be decided as we go
        // either, because textures bind while every material is still an archetype and the
        // translated ones only replace them once the shaders arrive.
        applyBaseMapColourSpace(texture, this.archetypeWants(wanted));

        root.traverse((node) => {
            if (!(node instanceof THREE.Mesh)) {
                return;
            }

            if (node.material instanceof AlamoMaterial) {
                node.material.bindTexture(name, texture);
                return;
            }

            const material = node.material as
                THREE.MeshStandardMaterial | THREE.MeshBasicMaterial;
            const spec = material.userData?.spec;
            if (spec === undefined) {
                return;
            }

            if (spec.textures.base?.toLowerCase() === wanted) {
                material.map = texture;
                material.color.setHex(0xffffff);
                material.needsUpdate = true;
            }

            // Only a lit material has a normal map to put this in; an unlit one has no normal to
            // perturb, and assigning it would be a silent no-op that reads as a working bind.
            if (
                spec.textures.normal?.toLowerCase() === wanted &&
                material instanceof THREE.MeshStandardMaterial
            ) {
                material.normalMap = texture;
                material.needsUpdate = true;
            }
        });
    }

    /**
     * Puts the current wireframe state onto whatever materials exist now.
     *
     * Called again after a part loads and after a renderer switch, because both replace materials
     * wholesale - and a flag set on the materials that came before is simply gone.
     */
    applyWireframe(): void {
        this.context.assembly.forEachDrawnMesh((mesh) => {
            for (const material of Array.isArray(mesh.material)
                ? mesh.material
                : [mesh.material]) {
                if (
                    material instanceof THREE.Material &&
                    "wireframe" in material
                ) {
                    (
                        material as THREE.Material & { wireframe: boolean }
                    ).wireframe = this.wireframe;
                }
            }
        });
    }
}
