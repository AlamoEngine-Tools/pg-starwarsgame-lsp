// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Stencil shadow volumes - Crow, 1977 - which is how the engine actually casts shadows.
//
// A model does not cast from its render geometry. The artist authors a separate low-poly SHADOW
// MESH, preprocessed so every edge carries a degenerate quad; the engine pushes the vertices facing
// away from the light out along the light direction, which opens those degenerate quads into the
// side walls of a closed volume. Everything inside that volume is in shadow - the model's own
// surface included, which is why self-shadowing comes free and why the shadow colour applies
// everywhere rather than only on the ground.
//
// The counting is the DEPTH-FAIL variant: for each pixel, front faces of the volume that fail the
// depth test increment a stencil counter and back faces that fail decrement it, so a non-zero
// counter means the eye ray crossed into the volume and did not come out again before hitting the
// surface. Depth-fail rather than Crow's original depth-pass because it is the one that stays
// correct when the camera is inside the volume - standing under a Star Destroyer, in other words.
//
// The engine's own render states (read off `MeshShadowVolume.fx`, technique `t0_zfail`): colour
// writes off, `ZFunc Less`, no depth write, no culling, two-sided stencil, front Incr on z-fail,
// back Decr on z-fail. Then a full-screen quad darkens whatever the stencil marked.
//
// Nothing of Petroglyph's shader text is reproduced here - this implements the published algorithm
// with the parameters their data implies.

import * as THREE from 'three';

/** Reused so a draw allocates nothing. */
const INVERSE_WORLD = new THREE.Matrix4();
const OBJECT_LIGHT = new THREE.Vector3();

/**
 * How far a vertex facing away from the light is pushed, as a multiple of the model's radius.
 *
 * NOT the engine's literal 1000. Depth-fail counting only works while the far cap of the volume
 * stays inside the view frustum: a fragment beyond the far plane is CLIPPED, not depth-failed, so
 * the volume is left open at the far end and the count leaks - which paints the whole screen as
 * shadowed. The engine can afford 1000 against a game-scale far plane; this preview fits its far
 * plane to the model, so the extrusion is scaled to the model instead. Four radii clears the ground
 * under anything while staying well inside a frustum fitted to the model and its effects.
 */
export const EXTRUSION_RADII = 4;

/**
 * How far the volume reaches for a model of this size.
 *
 * Scaled to the subject: a reach that suits a trooper leaves a Star Destroyer's shadow stopping in
 * mid-air. Capped by the FAR PLANE as well, not just by the model - depth-fail counting only holds
 * while the far cap is inside the frustum, because a fragment past the far plane is clipped rather
 * than depth-failed, which leaves the volume open and leaks the count across the background. Half
 * the far distance keeps the cap comfortably inside whatever the camera can see.
 */
export function volumeReach(radius: number, cameraFar: number): number {
    return Math.min(Math.max(radius, 1) * EXTRUSION_RADII, cameraFar * 0.5);
}

/**
 * Which stencil bits the count uses.
 *
 * The engine masks to six bits, which caps the nesting depth at 63 overlapping volumes - far more
 * than a model has - while leaving the top bits alone for anything else that wants the buffer.
 */
const STENCIL_MASK = 0x3f;

/**
 * The extrusion, in OBJECT space.
 *
 * The engine transforms the light into object space and compares it against the raw vertex normal
 * (`RenderEngine.cpp`: `light0ObjVector = normalize(-sunDirection * worldInv)`). Doing it the other
 * way round - transforming the NORMAL into world space - is not the same test once a bone chain or
 * a non-uniform scale is involved: vertices land on the wrong side of the split, so not every
 * degenerate edge quad opens, the volume is left with holes, and the stencil count leaks out across
 * the whole background.
 */
const VOLUME_VERTEX = /* glsl */`
uniform vec3 uToLightObject;
uniform float uExtrusion;

#include <common>
#include <skinning_pars_vertex>

void main() {
    vec3 transformed = position;
    vec3 objectNormal = normal;

    // The POSE comes first, both for the vertex and for its normal.
    //
    // A skinned volume is authored in bind space and carried into place by its bones, so the bind
    // normal says which way a face pointed before the model moved and the bind position is not
    // where the face ended up. Extruding first and skinning afterwards therefore hands each bone
    // its own rotated copy of the light direction, and the volume fans out into a different
    // direction per bone - which is exactly what an RSkin shadow looked like. Skinning first puts
    // both in the same posed object space the light is already expressed in.
    #include <skinbase_vertex>
    #include <skinnormal_vertex>
    #include <skinning_vertex>

    // A face turned away from the light is the far cap and is pushed out along the light; a face
    // towards it stays put, and the degenerate quad joining the two stretches into a side wall.
    //
    // Not a bare < 0.0. A face exactly perpendicular to the light sits on the knife edge, and the
    // quantised normals of one flat surface then straddle it - neighbouring triangles land on
    // opposite sides, the surface tears along every shared edge and the count stipples. The game's
    // art is axis-aligned and so is a light at a round azimuth, so this is the common case rather
    // than a corner one. Any consistent split is a valid volume, because every edge carries a quad
    // to bridge it, so near-perpendicular faces are simply all sent the same way.
    if (dot(objectNormal, uToLightObject) < -1e-4) {
        transformed -= uExtrusion * uToLightObject;
    }

    // Two steps, in this order, because that is exactly what three's own vertex shader does. All of
    // depth-fail counting rests on the volume's near cap landing at the SAME depth as the surface
    // it was authored on, so that ZFunc Less fails it and its increment cancels the far cap behind.
    // Folding the two matrices together first is the same value in algebra and a slightly different
    // one in floating point.
    vec4 mvPosition = modelViewMatrix * vec4(transformed, 1.0);
    gl_Position = projectionMatrix * mvPosition;
}
`;

/** Only ever seen in the debug view; the counting pass writes no colour at all. */
const VOLUME_FRAGMENT = /* glsl */`
uniform vec3 uDebugColour;
void main() { gl_FragColor = vec4(uDebugColour, 1.0); }
`;

/** A quad in clip space, so the darken covers the frame whatever the camera is doing. */
const DARKEN_VERTEX = /* glsl */`
void main() { gl_Position = vec4(position.xy, 0.0, 1.0); }
`;

const DARKEN_FRAGMENT = /* glsl */`
uniform vec3 uColour;

// The colorspace PARS chunk is deliberately not included: three already prepends it to every
// fragment shader, and including it again redefines its transfer functions, which fails the
// compile outright - and a material that will not compile simply draws nothing, so the shadow
// disappears with no error anywhere the reader can see.

void main() {
    gl_FragColor = vec4(uColour, 1.0);

    // Encoded like every other material's output, because this multiplies what they wrote and a
    // multiplier only means what it says in the same space as the thing it multiplies. Left raw,
    // the reader's 0.5 grey arrived as its LINEAR value - 0.216 - and halving turned into darkening
    // to a fifth, which is what made a lit hull come out nearly black. It also means the frame
    // going to the canvas and the frame going through the heat and bloom targets darken alike,
    // rather than by two different amounts depending on which effects happen to be on.
    #include <colorspace_fragment>
}
`;

/**
 * The volume pass and the darken that follows it.
 *
 * Everything lives in the SCENE graph rather than in a separate render call, ordered by
 * `renderOrder`: the volumes draw after the model, and the darken quad after them. That way one
 * `renderer.render` still produces the finished frame, and the pass works unchanged whether the
 * frame is going to the canvas, through the heat composite, or on to bloom.
 */
export class ShadowVolumePass {
    /** Front faces: increment where the volume is behind what was already drawn. */
    private readonly front: THREE.ShaderMaterial;

    /** Back faces: decrement, so a ray that leaves the volume again is not counted as inside. */
    private readonly back: THREE.ShaderMaterial;

    private readonly darkenMaterial: THREE.ShaderMaterial;

    /** The full-screen darken, added to the scene once. */
    readonly darken: THREE.Mesh;

    /** The extra meshes this pass owns, one per volume sub-mesh per face direction. */
    private readonly drawn: THREE.Mesh[] = [];

    /** Which counting meshes belong to which authored volume, so a level can gate them. */
    private readonly bySource = new Map<THREE.Mesh, THREE.Mesh[]>();

    /** The authored volumes the current detail level does NOT use. */
    private readonly uncounted = new Set<THREE.Mesh>();

    /** Whether the reader is in a mode that draws stencil shadows at all. */
    private enabled = false;

    /** The direction towards the light, in WORLD space; each draw takes it into its own. */
    private readonly worldLight = new THREE.Vector3(0, 1, 0);

    private extrusion = 1;

    private debug = false;

    constructor(private readonly renderOrder = 3000) {
        const uniforms = (): Record<string, THREE.IUniform> => ({
            uToLightObject: { value: new THREE.Vector3(0, 1, 0) },
            uExtrusion: { value: 1 },
            uDebugColour: { value: new THREE.Color(0, 1, 1) },
        });

        const volume = (zFail: THREE.StencilOp, side: THREE.Side): THREE.ShaderMaterial =>
            new THREE.ShaderMaterial({
                uniforms: uniforms(),
                vertexShader: VOLUME_VERTEX,
                fragmentShader: VOLUME_FRAGMENT,

                // The volume is a counting device, not something to look at.
                colorWrite: false,
                depthWrite: false,
                depthTest: true,
                depthFunc: THREE.LessDepth,
                side,

                // Pushed AWAY from the camera, which is what AloViewer's own SetDepthBias around
                // the volume phase does. Depth-fail counting leaves a lit surface lit only when the
                // volume's near cap FAILS the depth test against it, so its increment cancels the
                // far cap behind. The cap is a separate, coarser hull, so it lands a hair in front
                // as often as behind - and where it lands in front it passes instead, nothing
                // cancels, and the caster reads as inside its own shadow. Nudging the whole pass
                // back in DEPTH fixes that whichever way the surface happens to be turned, which
                // the sink along the light cannot: a face near perpendicular to the light slides
                // within its own plane and never separates. Measured on EV_LambdaShuttle: acne on
                // the lit side 11.8% to 3.4%, with the cast shadow on the ground unchanged.
                polygonOffset: true,
                polygonOffsetFactor: 1,
                polygonOffsetUnits: 2,

                stencilWrite: true,
                stencilRef: 1,
                stencilFuncMask: STENCIL_MASK,
                stencilWriteMask: STENCIL_MASK,
                stencilFunc: THREE.AlwaysStencilFunc,
                stencilFail: THREE.KeepStencilOp,
                stencilZPass: THREE.KeepStencilOp,
                stencilZFail: zFail,
            });

        // Two draws because a three.js material carries ONE set of stencil ops; the engine does the
        // same thing in one draw with two-sided stencil, which WebGL exposes but three does not.
        // The engine's own assignment. Which way round it is makes no difference to WHICH pixels
        // end up marked - the darken tests for a non-zero count, and swapping the two only flips
        // the sign - so this follows `t0_zfail` rather than inventing a convention.
        this.front = volume(THREE.IncrementWrapStencilOp, THREE.FrontSide);
        this.back = volume(THREE.DecrementWrapStencilOp, THREE.BackSide);

        this.darkenMaterial = new THREE.ShaderMaterial({
            uniforms: { uColour: { value: new THREE.Color(0.5, 0.5, 0.5) } },
            vertexShader: DARKEN_VERTEX,
            fragmentShader: DARKEN_FRAGMENT,

            // Multiply, not alpha: this DARKENS what is already there, so a mid grey halves the
            // brightness of whatever it covers. An alpha blend would paint a flat patch instead,
            // and a mid grey would then come out brighter than an unlit hull.
            blending: THREE.CustomBlending,
            blendEquation: THREE.AddEquation,
            blendSrc: THREE.ZeroFactor,
            blendDst: THREE.SrcColorFactor,

            depthTest: false,
            depthWrite: false,

            // Only where the count says the pixel is inside a volume.
            stencilWrite: true,
            stencilRef: 0,
            stencilFuncMask: STENCIL_MASK,
            stencilWriteMask: STENCIL_MASK,
            stencilFunc: THREE.NotEqualStencilFunc,
            stencilFail: THREE.KeepStencilOp,
            stencilZFail: THREE.KeepStencilOp,
            stencilZPass: THREE.KeepStencilOp,
        });

        this.darken = new THREE.Mesh(new THREE.PlaneGeometry(2, 2), this.darkenMaterial);
        this.darken.frustumCulled = false;
        this.darken.renderOrder = renderOrder + 1;
        this.darken.visible = false;
        this.darken.name = 'aetShadowDarken';
    }

    /**
     * Whether this model has a volume that the current detail level uses.
     *
     * A fact about the MODEL, not about the mode: it says the hull's shadow is the volume's job, so
     * the hull should stop casting into the shadow map. Whether stencil shadows are switched on at
     * all is the caller's own state, and weighing the two here would leave a model with volumes
     * casting nothing whatsoever in the mode that has no volumes.
     */
    get active(): boolean {
        return this.drawn.some(mesh => !this.uncounted.has(mesh.parent as THREE.Mesh));
    }

    /**
     * Adopts one volume sub-mesh: two extra meshes sharing its geometry, one per face direction.
     *
     * The source mesh keeps its own material and stays switched off - it is the cyan debug volume
     * the reader can tick on, and it must not be drawn by being reused here.
     */
    add(source: THREE.Mesh): void {
        const skinned = source instanceof THREE.SkinnedMesh;


        for (const [at, template] of [this.front, this.back].entries()) {
            // Cloned per mesh, not shared: the light vector is in the mesh's OWN object space now,
            // so one material cannot answer for two meshes that sit at different transforms.
            const material = template.clone();

            // Front cyan, back magenta, so the debug view says which side of the volume each
            // surface is - an open volume shows as one colour where the other should be.
            material.uniforms.uDebugColour.value.setRGB(at === 0 ? 0 : 1, 1, at === 0 ? 1 : 0);
            material.colorWrite = this.debug;
            material.depthWrite = this.debug;

            const mesh = skinned
                ? new THREE.SkinnedMesh(source.geometry, material)
                : new THREE.Mesh(source.geometry, material);

            if (mesh instanceof THREE.SkinnedMesh && skinned) {
                // The same skeleton and the same bind matrix as the volume it stands in for -
                // rebinding would recompute against a world matrix that has already moved.
                mesh.bind(source.skeleton, source.bindMatrix);
            }

            mesh.renderOrder = this.renderOrder;
            mesh.frustumCulled = false;
            mesh.userData.aetShadowVolume = true;
            mesh.name = `${source.name}#shadow`;

            // Per draw, because it depends on where this mesh has ended up. `onBeforeRender` is the
            // one hook that runs after the world matrices are up to date.
            mesh.onBeforeRender = (): void => {
                INVERSE_WORLD.copy(mesh.matrixWorld).invert();
                OBJECT_LIGHT.copy(this.worldLight).transformDirection(INVERSE_WORLD).normalize();
                material.uniforms.uToLightObject.value.copy(OBJECT_LIGHT);
                material.uniforms.uExtrusion.value = this.extrusion;
            };

            source.add(mesh);
            this.drawn.push(mesh);

            const siblings = this.bySource.get(source);
            if (siblings === undefined) {
                this.bySource.set(source, [mesh]);
            } else {
                siblings.push(mesh);
            }

            mesh.visible = this.enabled;
        }
    }

    /**
     * Whether this authored volume is one of the ones in play right now.
     *
     * An ALO carries a shadow mesh per ALT level, and the source meshes are switched off by LAYER
     * rather than by `visible` - see `meshVisibility.ts` for why. A counting mesh parented to a
     * source that is gated off by its layer therefore still drew, so a Lambda shuttle showing ALT0
     * was being marked by ALT1's volume on top of its own. The counting meshes are this class's
     * own, carry no layer rule, and are gated here instead.
     */
    setCounting(source: THREE.Mesh, on: boolean): void {
        if (!this.bySource.has(source)) {
            return;
        }

        if (on) {
            this.uncounted.delete(source);
        } else {
            this.uncounted.add(source);
        }

        this.refreshDrawn();
    }

    /**
     * Gives up one authored volume, for geometry leaving a scene that is still standing.
     *
     * Not {@link clear}: a piece of wreckage reaching the end of its lifetime and a death clone
     * removed by a repair both take their geometry with them while the hull beside them keeps its
     * shadow. Holding the source afterwards would keep the whole disposed subtree alive through
     * `bySource`, and the counting meshes would go on being drawn against buffers that have been
     * freed.
     */
    remove(source: THREE.Mesh): void {
        const meshes = this.bySource.get(source);

        if (meshes === undefined) {
            return;
        }

        for (const mesh of meshes) {
            mesh.removeFromParent();
        }

        this.drawn.length = 0;
        this.bySource.delete(source);
        this.uncounted.delete(source);

        for (const siblings of this.bySource.values()) {
            this.drawn.push(...siblings);
        }

        this.refreshDrawn();
    }

    /** `visible` is the product of the two gates, and only this writes it. */
    private refreshDrawn(): void {
        for (const [source, meshes] of this.bySource) {
            for (const mesh of meshes) {
                mesh.visible = this.enabled && !this.uncounted.has(source);
            }
        }

        this.darken.visible = this.enabled && this.active;
    }

    /** Drops every volume mesh, for a part being unloaded. */
    clear(): void {
        for (const mesh of this.drawn) {
            mesh.removeFromParent();
        }

        this.drawn.length = 0;
        this.bySource.clear();
        this.uncounted.clear();
        this.darken.visible = false;
    }

    /**
     * The direction TOWARDS the light, which is what decides which side of the volume a face is on.
     *
     * A plain vector rather than a `THREE.Vector3`, because that is what `lightDirection` hands out
     * and it already points from the model towards the light - the same value that places the key
     * light itself, so the two cannot disagree about where the shadow falls.
     */
    setLight(toLight: { x: number; y: number; z: number }): void {
        // Kept in WORLD space here; each draw takes it into its own object space, which is where
        // the engine does the comparison.
        this.worldLight.set(toLight.x, toLight.y, toLight.z).normalize();
    }

    /**
     * Sizes the volume to the subject.
     *
     * Called whenever the model's bounds change, for the same reason the shadow camera is.
     */
    setScale(radius: number, cameraFar: number): void {
        this.extrusion = volumeReach(radius, cameraFar);
    }

    setColour(colour: THREE.ColorRepresentation): void {
        this.darkenMaterial.uniforms.uColour.value.set(colour);
    }

    /**
     * Draws the extruded volume itself, as AloViewer's own "Debug Shadows" does.
     *
     * The only way to see what the counting is counting: a wrong shadow and a correct one look
     * identical from the stencil buffer, while an open or misdirected volume is obvious the moment
     * it is drawn. Front faces cyan, back faces magenta, so which side is which reads at a glance.
     */
    setDebug(on: boolean): void {
        this.front.colorWrite = on;
        this.back.colorWrite = on;
        this.front.depthWrite = on;
        this.back.depthWrite = on;
        this.debug = on;

        for (const mesh of this.drawn) {
            const material = mesh.material as THREE.ShaderMaterial;
            material.colorWrite = on;
            material.depthWrite = on;
        }

        this.darken.visible = !on && this.drawn.length > 0;
    }

    /** Draws the volumes and the darken, or neither. */
    setEnabled(on: boolean): void {
        this.enabled = on;
        this.refreshDrawn();
    }

    dispose(): void {
        this.clear();
        this.front.dispose();
        this.back.dispose();
        this.darkenMaterial.dispose();
        this.darken.geometry.dispose();
    }
}
