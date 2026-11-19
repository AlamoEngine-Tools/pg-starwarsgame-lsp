// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The viewport's particle systems: one instance per attached system, the fading ones finishing their lives,
// the textures they share, the emitters the reader has hidden, the wind and weather they are drawn in, and
// the per-frame step that runs them. It reads the parts, the levels and the material state; nothing else.

import {
    type Wind,
    DEFAULT_VIEWER_SETTINGS,
} from "../../shared/viewerSettings";
import { type ParticleEntry } from "./particleEntry";
import { ParticlePreparations } from "./particlePreparation";
import { ParticleSystemInstance, type EmitterProbe } from "./particleRenderer";
import * as THREE from "three";
import { type Vector3 as Vector3Like } from "./lighting";
import { type AlamoParticleContent } from "../../protocol/modelPreview";
import { proxyVisibleAt } from "./levels";
import { type VisibilityTrack, hiddenAt } from "./boneVisibility";
import { ACTIVE_SUBJECT } from "./loadedPart";
import { emissionMeshFor } from "./emissionSource";
import { missingTexture } from "./missingTexture";
import { type CaptureShot, type ParticlePlacement } from "./viewportTypes";
import { type ViewportContext } from "./viewportContext";

/** Splits a `systemId#index` emitter key. The id may itself contain a `#`, so split from the right. */
function splitEmitterKey(key: string): [string, number] {
    const at = key.lastIndexOf("#");
    return [key.slice(0, at), Number(key.slice(at + 1))];
}

export class ParticleRig {
    /** The wind as a vector in the space the particles are simulated in. */
    windVector: { x: number; y: number; z: number } = {
        x: 0,
        y: 0,
        z: 0,
    };
    /** The same wind in the terms the foliage shaders read it: a heading and a speed. */
    wind: Wind = DEFAULT_VIEWER_SETTINGS.wind;
    /** The weather phase's intensity, the energy every weather system is drawn at. */
    private weatherIntensity = DEFAULT_VIEWER_SETTINGS.weatherIntensity;
    readonly particleSystems = new Map<string, ParticleEntry>();
    /**
     * What each distinct system of this scene was found to need, worked out once however many
     * names it is filed under and however often it is placed (#436).
     */
    readonly preparations = new ParticlePreparations();
    /**
     * Systems that have been killed but whose particles are still finishing.
     *
     * `leaveParticles` on the SYSTEM decides this, and it is what the engine does:
     * `Engine::KillParticleSystem` calls `StopSpawning()` for one that says so and lets the air
     * clear, and `Kill()` for one that does not. It defaults TRUE, and the ones that say false are
     * the ENGINE GLOWS - a glow cuts with the engine, a fire trails off. Nothing read the flag, so
     * a wreck's fire vanished mid-flame the instant its debris expired.
     *
     * Held OUT of {@link particleSystems} rather than flagged inside it: they are no longer
     * addressable, must not be re-gated by anything, and must not collide with a new system taking
     * the same id.
     */
    private fadingParticles: ParticleSystemInstance[] = [];
    /** Textures the server has told us do not resolve. */
    private readonly missingTextures = new Set<string>();
    particlesVisible = true;
    private particlesPaused = false;
    /** One-shot systems - death explosions - removed once they burn out. */
    private readonly transient = new Set<string>();
    /**
     * Emitters switched off, keyed `systemId#index`.
     *
     * Held here rather than only in the shell because restart rebuilds every instance from
     * scratch; without this, a restart would silently switch them all back on. Keyed by index because
     * emitter names repeat within one system.
     */
    private readonly hiddenEmitters = new Set<string>();
    /**
     * Playback rate for particles only.
     *
     * Separate from the animation mixer's: a smoke plume worth slowing down to inspect usually sits on
     * a model whose animation you still want at speed.
     */
    private particleSpeed = 1;

    constructor(private readonly context: ViewportContext) {}

    /** Shows or hides each system by whether its proxy is tagged for the level the model is shown at. */
    gateByLevel(alt: number, lod: number, altDescending: boolean): void {
        for (const entry of this.particleSystems.values()) {
            entry.levelVisible = proxyVisibleAt(
                entry.levels,
                alt,
                lod,
                altDescending,
            );
        }
    }

    /** Disposes every system and forgets them all, the fading ones and the one-shots too, for a new scene. */
    forgetSystems(): void {
        for (const entry of this.particleSystems.values()) {
            entry.instance.dispose();
        }
        this.particleSystems.clear();
        // A new subject is one of the two things allowed to cut a fading system short.
        this.clearFadingParticles();
        this.transient.clear();
        this.preparations.clear();
    }

    /**
     * Runs every system one frame on, and finishes the ones fading out - nothing while effects are
     * off or paused. `view` is the camera's inverse world matrix, for the streaks measured on screen.
     */
    step(dt: number, view: THREE.Matrix4): void {
        if (this.particlesVisible && !this.particlesPaused) {
            for (const [id, entry] of [...this.particleSystems]) {
                // The camera goes in because a streak's length is a screen-space measurement:
                // a spark flying at the viewer has no tail to draw.
                entry.instance.update(
                    dt * this.particleSpeed,
                    view,
                    this.windVector,
                );

                // A death explosion is over once it stops emitting and its last spark dies.
                if (this.transient.has(id) && entry.instance.exhausted) {
                    this.transient.delete(id);
                    this.removeParticleSystem(id);
                }
            }

            // The ones already killed, finishing. Stepped in the SAME gate as the live systems, so
            // the global particle switch and the pause govern them identically - the reader turning
            // effects off must not leave a wreck's fire burning on.
            for (let at = this.fadingParticles.length - 1; at >= 0; at--) {
                const instance = this.fadingParticles[at];

                instance.update(dt * this.particleSpeed, view, this.windVector);

                // `empty`, not `exhausted`: it has been told to stop spawning, so "finished" will
                // never come for a continuous emitter. What is left is the only question.
                if (instance.empty) {
                    instance.dispose();
                    this.fadingParticles.splice(at, 1);
                }
            }
        }
    }

    /**
     * Every running particle system's emitters as their last frame left them, and where the camera
     * stands. For the UI tests, through the page's debug handle: whether a field is full, drifts,
     * stays frozen or is drawn back to front is a question about the simulation, not the pixels.
     */
    particleProbe(): {
        camera: Vector3Like;
        systems: { id: string; emitters: EmitterProbe[] }[];
    } {
        return {
            camera: this.context.cameraRig.cameraPosition,
            systems: [...this.particleSystems.values()].map((entry) => ({
                id: entry.id,
                emitters: entry.instance.probe(),
            })),
        };
    }

    /**
     * Sets the one wind the scene has.
     *
     * Read by the foliage shaders through their own semantics and by the 484 emitters that set
     * `affectedByWind`: an ordinary one gains half of it as speed every frame, a weather field
     * drifts with five times it - see `stepParticle` and the weather step.
     */
    setWind(headingDegrees: number, speed: number): void {
        this.wind = { heading: headingDegrees, speed };
        const heading = (headingDegrees * Math.PI) / 180;

        this.windVector = {
            x: Math.cos(heading) * speed || 0,
            y: 0,
            z: -Math.sin(heading) * speed || 0,
        };
    }

    /**
     * Sets the weather phase's intensity.
     *
     * Measured: a phase sets it as the energy of the weather system's group, so it reaches the
     * systems that hold a weather field and no other effect, which stays at 1.0.
     */
    setWeatherIntensity(intensity: number): void {
        this.weatherIntensity = intensity;
        for (const entry of this.particleSystems.values()) {
            if (entry.instance.hasWeather) {
                entry.instance.setEnergy(intensity);
            }
        }
    }

    /** Whether the scene holds a weather field, for the control that only reaches one. */
    hasWeather(): boolean {
        for (const entry of this.particleSystems.values()) {
            if (entry.instance.hasWeather) {
                return true;
            }
        }
        return false;
    }

    /**
     * Plays a system once and forgets it.
     *
     * A death explosion is an event, not a state: it fires as the hardpoint is destroyed and must not
     * come back when the effect list is toggled or the levels change.
     */
    playOnce(
        id: string,
        system: AlamoParticleContent,
        attachToPartId?: string,
        attachBone?: string,
        /** The owning object's uniform render scale. See {@link ParticlePlacement}. */
        scaleFactor = 1,
        /** Where to put it when it hangs on no part. See {@link ParticlePlacement.at}. */
        at?: { x: number; y: number; z: number },
    ): void {
        // Always the Gameplay lens's. A one-shot IS an event - a hardpoint blowing up - and an
        // event is never something the model declares.
        this.addParticleSystem(id, system, {
            origin: "gameplay",
            attachToPartId,
            attachBone,
            scaleFactor,
            at,
        });
        this.transient.add(id);
    }

    /**
     * Starts a particle system, optionally attached to a bone of a loaded part.
     *
     * Parented rather than positioned: a damaged hardpoint's smoke has to follow the hull as it
     * animates, and parenting gets that for nothing.
     */
    addParticleSystem(
        id: string,
        system: AlamoParticleContent,
        placement: ParticlePlacement,
    ): void {
        const {
            attachToPartId,
            attachBone,
            attachBoneIndex,
            origin,
            scaleFactor = 1,
            at,
            levels = { alt: null, lod: null, altDecreaseStayHidden: false },
        } = placement;

        // Immediate: the id is about to name something else, and a fading twin under it would be
        // orphaned in the scene for good.
        this.removeParticleSystem(id, true);

        const entry: ParticleEntry = {
            id,
            system,
            attachToPartId,
            attachBone,
            attachBoneIndex,
            gateVisible: true,
            levels,
            levelVisible: proxyVisibleAt(
                levels,
                this.context.levels.alt,
                this.context.levels.lod,
                this.context.levels.altDescending,
            ),
            scaleFactor,
            origin,
            instance: this.instantiate(
                id,
                system,
                attachToPartId,
                attachBone,
                attachBoneIndex,
                scaleFactor,
                at,
            ),
        };

        // Held from birth when it is attached to something already off screen: a death clone's effects
        // attach while the clone is still hidden, and a one-shot that runs there is spent before
        // anyone sees it.
        if (
            attachToPartId !== undefined &&
            this.context.assembly.hiddenParts.has(attachToPartId)
        ) {
            entry.instance.hold("part", true);
        }

        this.particleSystems.set(id, entry);
        if (entry.instance.hasWeather) {
            entry.instance.setEnergy(this.weatherIntensity);
        }
        this.refreshParticleVisibility();

        // The depth range again, because the effects reach further than the model and they arrive
        // AFTER the camera was fitted to it. Without this, Boba Fett's flamethrower - 206 units of
        // throw on a seven-unit model - was culled at 156 and looked like a flame that stops.
        this.context.cameraRig.applyClipPlanes(
            this.context.cameraRig.boundingSphere(),
            this.context.cameraRig.fitDistance,
        );
    }

    // ── levels ────────────────────────────────────────────────────────────────

    /**
     * Takes a system out of the scene.
     *
     * What it does with the particles ALREADY IN THE AIR is the file's word: see
     * {@link fadingParticles}. `immediate` overrides it, and only two things may - replacing a
     * system under the same id, and a wholesale reset of the scene or the renderer.
     */
    removeParticleSystem(id: string, immediate = false): void {
        const entry = this.particleSystems.get(id);

        if (entry !== undefined) {
            if (
                !immediate &&
                entry.system.leaveParticles &&
                !entry.instance.empty
            ) {
                // Stop spawning and let the air clear. Nothing sweeps it after this, which is why
                // it leaves `particleSystems` - a gate re-enabling it would refill it forever.
                entry.instance.hold("released", true);

                // And cut loose from the bone it was anchored on, which is usually about to be
                // hidden or disposed. Nothing moves: the simulation already runs in the model's
                // space with the bone cancelled out, so the particles stay exactly where they are -
                // but a bone still being read after its part is gone would report its last stale
                // matrix as this step's movement and fling every linked particle with it.
                entry.instance.detach();

                this.fadingParticles.push(entry.instance);
            } else {
                entry.instance.dispose();
            }
        }

        this.particleSystems.delete(id);

        // And back in again once the effect is gone, so a one-off explosion does not leave the
        // depth range stretched for the rest of the session.
        this.context.cameraRig.applyClipPlanes(
            this.context.cameraRig.boundingSphere(),
            this.context.cameraRig.fitDistance,
        );
    }

    /** Names of the running systems, for the emitter list in the dock. */
    particleSystemIds(): string[] {
        return [...this.particleSystems.keys()].sort();
    }

    /** Every texture the running systems want but may not have yet. */
    particleTextureNames(): string[] {
        return [
            ...new Set(
                [...this.particleSystems.values()].flatMap((e) =>
                    e.instance.textureNames(),
                ),
            ),
        ].sort();
    }

    /** Hands a fetched texture to whichever emitters named it, now and after a restart. */
    setParticleTexture(name: string, texture: THREE.Texture): void {
        // The same store the materials read: a name is one texture whichever kind of thing
        // samples it, and the store is what lets it go.
        this.context.textures.set(name, texture);
        this.missingTextures.delete(name.toLowerCase());

        for (const entry of this.particleSystems.values()) {
            entry.instance.setTexture(name, texture);
        }
    }

    /**
     * Says a texture will never arrive, so every emitter naming it shows the marker.
     *
     * Remembered, not just applied: a system can be built after the answer came back - a hardpoint
     * blowing up spawns one - and it has to show the marker too rather than sitting on a blank quad
     * because it happened to be late.
     */
    setParticleTextureMissing(name: string): void {
        this.missingTextures.add(name.toLowerCase());

        void this.markMissing(name);
    }

    setParticlesVisible(visible: boolean): void {
        this.particlesVisible = visible;

        // A system switched off individually, or by its level, stays off when the global toggle
        // comes back on. One refresh answers for every system: it re-runs the whole row chain, which
        // the old per-system loop did once per system to the same end.
        if (this.particleSystems.size > 0) {
            this.refreshParticleVisibility();
        }
    }

    /** Shows or hides the effects as a still capture asks, and returns what puts them back. */
    stageForCapture(shot: CaptureShot): () => void {
        const before = this.particlesVisible;

        this.setParticlesVisible(shot.particles);

        return () => this.setParticlesVisible(before);
    }

    /**
     * Re-aims every shown sprite at the camera as it now stands, without moving the effects on.
     *
     * A sprite is oriented against the VIEW matrix - a streak's length is a screen-space measurement -
     * so a camera moved outside the frame step leaves it aimed at the old one. A zero step re-aims
     * without advancing the simulation.
     */
    reaimAtCamera(): void {
        if (!this.particlesVisible) {
            return;
        }

        for (const entry of this.particleSystems.values()) {
            entry.instance.update(
                0,
                this.context.camera.matrixWorldInverse,
                this.windVector,
            );
        }
    }

    /**
     * What the game's rules say about a system: the opening rule and the current damage state.
     *
     * The RULES path, called on attach and again whenever a hardpoint is destroyed or repaired -
     * never in response to a click. A hand-set override survives this, so switching one effect on
     * does not get undone by an unrelated hardpoint blowing up.
     *
     * What the rules say is `inFile` for this row - the model's own word - and the chain weighs it
     * against everything else. It does NOT clear a hand-set override: the reader's word is taken
     * back only by the reader or by a clip starting, and a row standing against the rules is marked
     * as one the model does not draw, so nothing here is claimed that is not true.
     */
    setParticleSystemVisible(id: string, visible: boolean): void {
        const entry = this.particleSystems.get(id);
        if (entry === undefined) {
            return;
        }

        entry.gateVisible = visible;
        this.context.rows.refreshRows();
    }

    /** The emitters of one system, in file order. */
    emitterNames(systemId: string): string[] {
        return (
            this.particleSystems.get(systemId)?.instance.emitterNames() ?? []
        );
    }

    /** Shows or hides one emitter of one system, by its position in that system's file. */
    setEmitterVisible(systemId: string, index: number, visible: boolean): void {
        const key = `${systemId}#${index}`;

        if (visible) {
            this.hiddenEmitters.delete(key);
        } else {
            this.hiddenEmitters.add(key);
        }

        this.particleSystems
            .get(systemId)
            ?.instance.setEmitterVisible(index, visible);
    }

    setParticlesPaused(paused: boolean): void {
        this.particlesPaused = paused;
    }

    /** Multiplier on particle time. 1 is real time. */
    setParticleSpeed(speed: number): void {
        this.particleSpeed = Math.max(0, speed);
    }

    /**
     * Replays every system from nothing.
     *
     * Rebuilt rather than rewound: a burst emitter's whole point is the first half second, and there
     * is no way back to it from the middle of a run short of discarding the state.
     */
    restartParticles(): void {
        // The other override: every system is being rebuilt from scratch, and a fading one has no
        // rebuilt twin to belong to.
        this.clearFadingParticles();

        for (const [id, entry] of this.particleSystems) {
            entry.instance.dispose();
            entry.instance = this.instantiate(
                id,
                entry.system,
                entry.attachToPartId,
                entry.attachBone,
                entry.attachBoneIndex,
                entry.scaleFactor,
            );
            this.refreshParticleVisibility();
        }
    }

    /**
     * Holds the effects attached to a part while that part is off screen.
     *
     * An emitter nobody is drawing must not spawn - the rule `EmitterRenderer` already follows one
     * level down, and the reason its clock is held with it. It was never applied to a whole PART,
     * and a death clone is exactly the case that needs it: the clone is fetched early and held
     * hidden so its arrival is not a visible gap, which gave its effects a free run at a model
     * nobody could see. Its explosions are ONE-SHOTS - `p_explosion_big00` fires a single burst of
     * 50 sparks - so by the time the ship actually died they were spent, and the blast that covers
     * the wreck breaking up drew two stray particles.
     *
     * Only ever HOLDS here. Releasing is the clip's business: `advancePassiveSubjects` gates a
     * playing subject frame by frame, and a subject with no clip is released as it is shown.
     */
    holdEffectsOf(partId: string): void {
        const hidden = this.context.assembly.hiddenParts.has(partId);

        for (const entry of this.particleSystems.values()) {
            if (entry.attachToPartId === partId) {
                entry.instance.hold("part", hidden);
            }
        }
    }

    // ── skeleton ───────────────────────────────────────────────────────

    /**
     * Draws or holds one system, as the row chain decided.
     *
     * The chain has already weighed the effects master, the level gate and the reader, so this only
     * carries the answer - it must not weigh any of them a second time.
     */
    setParticleSystemDrawn(id: string, visible: boolean): void {
        this.particleSystems.get(id)?.instance.setVisible(visible);
    }

    /**
     * Holds the emitters whose proxy bone ONE SUBJECT's animation has switched off.
     *
     * Hiding the bone NODE is not enough for a particle system: the system's mesh is parented to the
     * bone, so it would stop being drawn, but it would go on emitting the whole time and burst into
     * view fully populated the moment the bone came back. 2285 of the shipped visibility tracks are
     * particle proxies, so this is the case the whole feature exists for - the rancor's death
     * explosion must not already be a fireball when frame 26 arrives.
     *
     * SCOPED to one subject, which it was not. It swept every system in the scene and gated them all
     * on the ACTIVE subject's clip - the last scene-wide map driven by the active subject that the
     * multi-subject work had not reached. With no active clip running, `bits` is undefined for
     * everything and this set `setEmitting(true)` on every system in the scene, every frame, which
     * also overrode any answer a passive subject had reached for its own.
     *
     * What that cost: a death clone's `_die` clip is a CHOREOGRAPHY - the Mon Calamari Cruiser's
     * times four debris bursts to 0.3-2.0s, ten big explosions across the minute, and three
     * `p_explosion_huge00` to 59.7s as the hull's bones go dark at 59.9s. All 39 of its systems ran
     * continuously from the moment it spawned instead, and the clip only alternated whether you
     * could see them.
     */
    applyEmitterGating(
        subjectId: string,
        track: VisibilityTrack | undefined,
        action: THREE.AnimationAction | null,
    ): void {
        for (const entry of this.particleSystems.values()) {
            if (
                entry.attachBone === undefined ||
                entry.attachBoneIndex === undefined
            ) {
                continue;
            }

            if (this.subjectOfSystem(entry) !== subjectId) {
                continue;
            }

            const bits = track?.bones.get(
                `${entry.attachBone}#${entry.attachBoneIndex}`,
            );

            // A clip that says nothing about this proxy lifts only the CLIP's hold. It is not a
            // permission to spawn: the part this system is attached to may be hidden, and this runs
            // every frame, so voting yes here re-armed a held emitter one frame after it was held.
            entry.instance.hold(
                "clip",
                bits !== undefined &&
                    action !== null &&
                    hiddenAt(bits, track!.fps, action.time),
            );
        }
    }

    /**
     * Hands one subject's effects over to finish on their own.
     *
     * Each goes through {@link removeParticleSystem}, so the file's `leaveParticles` still decides
     * whether it drains or is cut - an engine glow goes out with the ship, an explosion does not.
     */
    releaseSubjectEffects(subjectId: string): void {
        for (const [id, entry] of [...this.particleSystems]) {
            if (this.subjectOfSystem(entry) === subjectId) {
                this.removeParticleSystem(id);
            }
        }
    }

    /**
     * The subject a particle system belongs to: the subject of the part it is attached to.
     *
     * A system with no part named is the scene's own - a particle file opened directly - and that is
     * the active subject by definition.
     */
    private subjectOfSystem(entry: ParticleEntry): string {
        return entry.attachToPartId === undefined
            ? ACTIVE_SUBJECT
            : (this.context.parts.get(entry.attachToPartId)?.subjectId ??
                  ACTIVE_SUBJECT);
    }

    private instantiate(
        id: string,
        system: AlamoParticleContent,
        attachToPartId?: string,
        attachBone?: string,
        attachBoneIndex?: number,
        scaleFactor = 1,
        at?: { x: number; y: number; z: number },
    ): ParticleSystemInstance {
        // A marker at `at` where one was asked for and nothing is being hung on: the wreck's own
        // explosion has to go off where the wreck FINISHED, and a part-less system otherwise
        // anchors at the model root. An empty node rather than moving the instance, so the anchor
        // stays the one thing that decides where a system sits.
        const anchor =
            at === undefined || attachToPartId !== undefined
                ? null
                : new THREE.Object3D();

        if (anchor !== null && at !== undefined) {
            anchor.position.set(at.x, at.y, at.z);
            this.context.modelRoot.add(anchor);
        }

        const attachment =
            anchor ??
            this.context.assembly.attachmentFor(
                attachToPartId,
                attachBone,
                attachBoneIndex,
            );

        const prepared = this.preparations.of(system);

        // Read once, here, rather than per spawn: the walk up the graph and the transform into the
        // attachment's space are the same answer every frame, and this runs thousands of times a
        // second on a `EveryVertex` emitter. `restartParticles` re-reads it, which is what picks up
        // a level switch that swapped the geometry underneath.
        //
        // And only for a system that is born on a mesh at all. The read copies every vertex of the
        // geometry the bone belongs to, which on a capital ship is the hull, and an emitter that
        // is not born on a mesh never looks at it.
        const source = prepared.emitsFromMesh
            ? emissionMeshFor(
                  attachment,
                  this.context.assembly.partRootOf(attachToPartId),
              )
            : null;

        // The model root is the space the particles are simulated in, so an effect the file does
        // not link to its emitter is genuinely left behind rather than dragged along by the bone
        // it is anchored on.
        const instance = new ParticleSystemInstance(
            id,
            system,
            this.context.textures.byName,
            1,
            source,
            this.context.modelRoot,
            scaleFactor,
            prepared,
        );
        instance.attachTo(attachment);
        instance.setVisible(this.particlesVisible);
        // Built after its effects came back, as a hardpoint's blast is: it takes what is in hand.
        instance.applyEffects(
            this.context.materials.translated,
            this.context.materials.shaderStates,
            this.context.materials.translatedShaders,
        );

        // A system built after the answers came back still has to show the marker: a hardpoint
        // blowing up spawns one long after the textures were asked for.
        for (const name of this.missingTextures) {
            void this.markMissing(name);
        }

        for (const key of this.hiddenEmitters) {
            const [systemId, index] = splitEmitterKey(key);
            if (systemId === id) {
                instance.setEmitterVisible(index, false);
            }
        }

        // Attached to the SIMULATION SPACE rather than to the bone it is anchored on. A proxy bone
        // is a child of the piece it belongs to - `p_explosion_big00#12` sits under `Busted_00#11`
        // on the Nebulon-B - and the death clip hides that piece on the very frame the blast
        // fires. three prunes a hidden subtree, so parenting the effect there cut it one frame in.
        // Anchoring is what makes it follow the bone; see `ParticleSystemInstance.attachTo`.
        //
        // Flagged all the same, so the model-mesh walk prunes it wherever it is reached from.
        instance.root.userData.aetParticleSystem = true;
        this.context.modelRoot.add(instance.root);
        return instance;
    }

    /** Drops every fading system at once, for a reset that takes the whole scene with it. */
    clearFadingParticles(): void {
        for (const instance of this.fadingParticles) {
            instance.dispose();
        }

        this.fadingParticles = [];
    }

    /** How far the running effects get from the model's centre. */
    effectReach(): number {
        let reach = 0;

        for (const entry of this.particleSystems.values()) {
            reach = Math.max(reach, entry.instance.extent());
        }

        return reach;
    }

    private async markMissing(name: string): Promise<void> {
        const marker = await missingTexture();

        // Still missing? A real texture can land while the marker is being decoded, and the answer
        // that arrived last is the one that is true.
        if (!this.missingTextures.has(name.toLowerCase())) {
            return;
        }

        for (const entry of this.particleSystems.values()) {
            entry.instance.setMissingTexture(name, marker);
        }
    }

    private refreshParticleVisibility(): void {
        // Deliberately does not decide anything itself. A system's row is in the chain like every
        // other row - the effects master vetoes it, its level gates it, the reader overrides it -
        // and a second opinion here would silently undo the first.
        this.context.rows.refreshRows();
    }
}
