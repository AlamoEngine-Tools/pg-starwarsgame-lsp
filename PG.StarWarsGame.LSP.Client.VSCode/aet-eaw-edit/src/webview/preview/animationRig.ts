// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The viewport's animation: each subject's mixer and clips kept apart, the clip playing with its speed,
// loop and pause, seeking and the rest pose, a death clip, the passive subjects stepped on their own, the
// bone visibility a clip drives, and the skin palette built per frame. It reads the rows, the particle
// systems and the assembly; nothing else.

import { type SubjectAnimation } from "./subjectAnimation";
import { ACTIVE_SUBJECT } from "./loadedPart";
import { deathClip, isDeathClipName } from "./visibility";
import {
    type DeathEnding,
    deathState,
    stepDeath,
    UNWRITTEN_ENDING,
} from "./deathEnding";
import * as THREE from "three";
import { visibilityTracks } from "./boneVisibility";
import { clipDone, playedLooping, playedOnce } from "./clipEnd";
import { CloakFade, type Look, PLAIN_LOOK } from "./cloakFade";
import { alamoNodeName } from "./skeleton";
import { type GLTF } from "three/examples/jsm/loaders/GLTFLoader.js";
import { type ViewportContext } from "./viewportContext";

/** Scratch for the bone-palette maths, so a per-frame loop allocates nothing. */
const SKIN_WORK = new THREE.Matrix4();

export class AnimationRig {
    /** Reused per frame so building the bone palette does not allocate on every draw. */
    private skinScratch = new Float32Array(0);
    /**
     * Every subject's animation state, keyed by subject id.
     *
     * These five things used to be one scene-wide set of maps, and every one of them was
     * "first wins" - which the hull always was. A death clone is the SAME MODEL as the ship it
     * replaces, so every node name, every clip name and every track name collided with the hull's:
     * a clone's `_die` played on the shared mixer drove the HULL's bones, and its visibility track
     * registered under the shared map hid the HULL's meshes. That collision is the whole reason a
     * second, parallel clip path existed. Per subject, the names are unique again and there is one
     * path.
     */
    readonly subjects = new Map<string, SubjectAnimation>();
    // Held here rather than read off the action, because they have to survive the action being torn
    // down and rebuilt - which happens on every clip change. Setting the loop mode on the panel and
    // then picking a different clip used to silently drop it.
    private animationSpeed = 1;
    private animationLoop = true;
    private animationPaused = false;
    /** Whether a clip is currently driving bone visibility, so the release happens exactly once. */
    private overridingVisibility = false;
    /** What to do when each dying subject is destroyed, by subject id - see {@link startDeathClip}. */
    private readonly deathEnds = new Map<string, () => void>();
    /**
     * The subject's cloak coming in and going, stepped by the viewport - see `cloakFade.ts`. The
     * subject's alone: a death clone is another object and wears none of it.
     */
    readonly cloak = new CloakFade();

    constructor(private readonly context: ViewportContext) {}

    /**
     * Takes in what one part brings to its subject's animation: the rest pose of every node, the
     * nodes by name, the meshes its file hides, and its clips with their visibility tracks.
     */
    registerPart(subjectId: string, root: THREE.Object3D, gltf: GLTF): void {
        const subject = this.subjectAnimation(subjectId);

        // Before any clip can touch it. Recorded for the whole part, not only for the bones this
        // GLB's own clips drive - a later part of the same subject can reach in here too.
        root.traverse((node) => {
            if (!subject.restPose.has(node)) {
                subject.restPose.set(node, {
                    position: node.position.clone(),
                    quaternion: node.quaternion.clone(),
                    scale: node.scale.clone(),
                });
            }

            // First wins. A visibility track names a bone, and a bone name is unique within one
            // model because the exporter suffixes it with the bone index. Keyed by the name the
            // exporter wrote, which is the one a track names - three's sanitised copy is not.
            const name = alamoNodeName(node);
            if (name !== "" && !subject.nodeByName.has(name)) {
                subject.nodeByName.set(name, node);
            }
        });

        if (gltf.animations.length > 0) {
            // Rooted at the first of this subject's parts to bring clips. A clip binds to nodes BY
            // NAME from the root it was given, so a mixer rooted anywhere else drives the wrong
            // model - which is exactly what a clone's `_die` did on the scene-wide mixer.
            subject.mixer ??= new THREE.AnimationMixer(root);
            subject.clips = [...subject.clips, ...gltf.animations];

            // Off the raw glTF JSON: three's loader builds AnimationClips and does not carry an
            // animation's extras onto them, so the parser's own document is the only source.
            for (const [name, track] of visibilityTracks(gltf.parser.json)) {
                subject.visibility.set(name, track);
            }
        }
    }

    /** Lets go of everything a subtree registered with its subject, before the subtree is disposed. */
    unregisterPart(subjectId: string, nodes: readonly THREE.Object3D[]): void {
        const subject = this.subjectAnimation(subjectId);

        for (const node of nodes) {
            subject.restPose.delete(node);

            // Only where THIS node is the one holding the name. `nodeByName` is first-wins, so one
            // part of a subject must not take another part's entry with it.
            const name = alamoNodeName(node);
            if (name !== "" && subject.nodeByName.get(name) === node) {
                subject.nodeByName.delete(name);
            }
        }
    }

    /** Forgets a passive subject whose last part has gone. */
    forgetSubject(subjectId: string): void {
        this.subjects.delete(subjectId);
        this.deathEnds.delete(subjectId);
    }

    /**
     * Steps the active subject's clip on by `dt` seconds.
     *
     * Through the turret layer while a clip is loaded, so every turret turns on top of the pose the
     * clip gives it. With no clip the mixer writes nothing, and there is nothing to compose on.
     */
    advance(dt: number): void {
        this.stepMixer(dt);
    }

    private stepMixer(dt: number): void {
        const mixer = this.active.mixer;

        if (mixer === null) {
            return;
        }

        if (this.active.action === null) {
            mixer.update(dt);
            return;
        }

        this.context.turret.composeOnClip(() => mixer.update(dt));
    }

    /** Drops every subject's animation and the visibility a clip was driving, for a new scene. */
    forgetSubjects(): void {
        this.subjects.clear();
        this.deathEnds.clear();
        this.cloak.reset();
        this.overridingVisibility = false;
    }

    /**
     * One subject's whole look - the tint and the alpha every mesh and effect of it is drawn with:
     * the subject's cloak, or a dying object's fade - see `deathEnding.ts`.
     */
    lookOf(subjectId: string): Look {
        if (subjectId === ACTIVE_SUBJECT) {
            return this.cloak.current();
        }

        const alpha = this.subjects.get(subjectId)?.death?.alpha ?? 1;

        return alpha === 1 ? { ...PLAIN_LOOK } : { ...PLAIN_LOOK, a: alpha };
    }

    get animationNames(): string[] {
        return this.active.clips.map((c) => c.name);
    }

    // ── content ───────────────────────────────────────────────────────────────

    /**
     * The subject the panel speaks for.
     *
     * Every animation control - the picker, the speed, the scrub bar, the loop switch - describes
     * the model that was opened, and only that one. A passive subject's clip is not something the
     * reader chose; it is what dying looks like, and it runs on its own terms.
     */
    get active(): SubjectAnimation {
        return this.subjectAnimation(ACTIVE_SUBJECT);
    }

    /**
     * Starts a passive subject dying: its own death clip, once, and the clock towards its end.
     *
     * ONCE, and clamped: the user's word is that a death clone is spawned at the exact position of
     * the model it replaces and plays through a single time. There is no picker for it and no loop
     * - it is not something the reader chose, it is what dying looks like.
     *
     * It then STAYS, on the clip's held frame, and ends as `ending` says - see `deathEnding.ts`.
     * `onEnd` runs once, the moment it is destroyed: that is when `Death_Explosions_End` go off.
     * A subject with no clip to play starts its persistence at once.
     *
     * Separate from LOADING it so the geometry can be fetched early and held: asking for a capital
     * ship's three-megabyte clone at the moment of death left a visible gap between the ship going
     * and the wreck arriving. Idempotent - a second call does not restart a subject already dying,
     * because it is the same wreck.
     *
     * This is `play` for a subject nobody is driving. It is the same machinery: the subject's own
     * mixer, the subject's own clips, the subject's own tracks. It used to be a second
     * implementation of all three, because the scene-wide maps it would have had to use were
     * already full of the hull's answers.
     */
    startDeathClip(
        subjectId: string,
        ending: DeathEnding = UNWRITTEN_ENDING,
        onEnd?: () => void,
    ): void {
        const subject = this.subjects.get(subjectId);

        if (subject === undefined || subject.death !== null) {
            return;
        }

        if (onEnd !== undefined) {
            this.deathEnds.set(subjectId, onEnd);
        }

        const name = deathClip(subject.clips.map((c) => c.name));
        const clip = subject.clips.find((c) => c.name === name);

        // With no clip to play, the clip is over from the start, and so is the wait for it.
        subject.death = deathState(
            ending,
            subject.mixer === null || clip === undefined,
        );

        if (subject.mixer === null || clip === undefined) {
            return;
        }

        // Its play-once copy, which ends on the held frame - see `clipEnd.ts`.
        subject.action = subject.mixer.clipAction(playedOnce(clip));
        subject.action.setLoop(THREE.LoopOnce, 1);
        // Without this the held pose is gone the frame it arrives.
        subject.action.clampWhenFinished = true;
        subject.action.reset();
        subject.action.play();
    }

    /**
     * Plays a clip by name, or stops playback when given null.
     *
     * Timing comes from the file. The exporter writes each keyframe at `frame / fps` using the rate
     * the `.ala` itself declares - 30 fps for 1270 of the 1363 shipped animations, but 1, 5, 10 or
     * 15 for the other 93 - so 1.0x means "this clip's own declared rate", whatever that is: a
     * 15 fps clip plays at 15 frames a second. There is no rate for the panel to impose.
     *
     * The rest pose is restored before anything starts, so a clip never plays on top of the pose
     * another one left behind.
     *
     * Answers whether a clip actually started.
     *
     * The answer matters and used to be thrown away. A name no loaded clip carries returned here in
     * silence while the panel went on showing that clip as the one playing - which is exactly what
     * an ability's deploy did on every model, since the XML names an `.ala` FILE and the clips are
     * held under their stems. Silence is right for the viewport, which cannot know whether a miss is
     * a fault; reporting it lets the caller stop claiming otherwise.
     */
    play(name: string | null): boolean {
        this.active.action?.stop();
        this.active.action = null;
        this.resetPose();

        // The pose is not the only thing a clip resets. Watching an animation means asking whether
        // the animation works, and that can only be answered against the model as its author left
        // it - so every row goes back to what the model says. The master toggles are untouched:
        // effects switched off because they obstruct the view are the one thing worth keeping off
        // while a clip runs. See `clearedForPlayback`.
        this.context.rows.clearRowOverrides();

        if (name === null || this.active.mixer === null) {
            return false;
        }

        const clip = this.active.clips.find((c) => c.name === name);
        if (clip === undefined) {
            return false;
        }

        this.active.action = this.startAction(
            this.active.mixer,
            clip,
            0,
            this.animationPaused,
        );

        return true;
    }

    /**
     * Starts the active subject's clip at a playhead, looping or not as the panel says.
     *
     * A clip that does not loop plays its play-once copy, which ends on the second-to-last frame and
     * holds it - the game's rule, see `clipEnd.ts`; a looping one plays the clip itself and wraps at
     * the last frame. Clamped, because without it a non-looping action snaps back to frame 0 the
     * instant it ends, and the held pose - often the whole point of a death or a deploy - is never on
     * screen.
     */
    private startAction(
        mixer: THREE.AnimationMixer,
        clip: THREE.AnimationClip,
        time: number,
        paused: boolean,
    ): THREE.AnimationAction {
        const action = mixer.clipAction(
            this.animationLoop ? playedLooping(clip) : playedOnce(clip),
        );
        action.setLoop(
            this.animationLoop ? THREE.LoopRepeat : THREE.LoopOnce,
            Infinity,
        );
        action.clampWhenFinished = true;
        action.timeScale = this.animationSpeed;
        action.paused = paused;
        action.play();
        action.time = Math.min(time, action.getClip().duration);

        return action;
    }

    // ── spinning away ─────────────────────────────────────────────────────────

    setPaused(paused: boolean): void {
        this.animationPaused = paused;

        if (this.active.action !== null) {
            this.active.action.paused = paused;
        }
    }

    /**
     * Puts every animated node back where the model file had it.
     *
     * three.js does restore a binding's "original state" when an action is deactivated - but the
     * original it restores is whatever the node held when that binding was FIRST created, and a
     * binding for a clip you start mid-way through another clip captures the other clip's pose. So
     * stopping left the model in a pose no file describes, and switching clips carried the previous
     * one's residue into every bone the new clip does not drive.
     *
     * Snapshotting at load and writing it back explicitly sidesteps all of that: the rest pose is
     * the file's pose, by definition, and it cannot drift.
     */
    resetPose(): void {
        for (const [node, rest] of this.active.restPose) {
            node.position.copy(rest.position);
            node.quaternion.copy(rest.quaternion);
            node.scale.copy(rest.scale);
        }

        // The file's pose is not the whole of a turret's: its rest angle and the reader's aim turn on
        // top of it, and they are the turret layer's to put back. Left out, starting any clip - an
        // ability's deploy included - swung every turret back to where it was exported.
        this.context.turret.reposeTurrets();

        // Transforms are the part three.js would have restored on its own; visibility is not, and a
        // bone left switched off by the clip that just stopped is the residue that actually shows.
        // Asking the level and override rules again is what puts it back - they, not this, know
        // what the model looked like before the clip took over.
        if (this.overridingVisibility) {
            this.overridingVisibility = false;
            this.context.levels.applyLevels();
        }

        // Only the CLIP's hold: the clip has stopped having an opinion, which says nothing about a
        // part that is hidden or a system that has been released to finish.
        for (const entry of this.context.particles.particleSystems.values()) {
            entry.instance.hold("clip", false);
        }
    }

    /** How fast the clip runs, as a multiple of the rate its file declares. */
    setAnimationSpeed(speed: number): void {
        this.animationSpeed = speed;

        if (this.active.action !== null) {
            this.active.action.timeScale = speed;
        }
    }

    setAnimationLoop(loop: boolean): void {
        this.animationLoop = loop;

        const old = this.active.action;
        const mixer = this.active.mixer;

        if (old === null || mixer === null) {
            return;
        }

        const clip = old.getClip();
        if (clip === (loop ? playedLooping(clip) : playedOnce(clip))) {
            return;
        }

        // The two play different copies of the clip, since each ends somewhere else, so switching
        // swaps the action - at the same playhead, paused or not as it was. A clip that has finished
        // was paused by the mixer, and stays held until it is played on.
        const time = old.time;
        const paused = old.paused;
        old.stop();
        this.active.action = this.startAction(mixer, clip, time, paused);
        // The stop put the bones back where their bindings began; this puts them at the playhead.
        this.stepMixer(0);
    }

    /**
     * Moves the playhead, in seconds.
     *
     * `paused` is left alone: scrubbing a running clip should keep it running from where you
     * dropped it, and scrubbing a held one should leave it held on the frame you chose.
     */
    seekAnimation(seconds: number): void {
        if (this.active.action === null) {
            return;
        }

        this.active.action.time = Math.max(
            0,
            Math.min(seconds, this.active.action.getClip().duration),
        );
        // One update with no time step, so the pose on screen matches the playhead immediately
        // rather than at the next frame - which is what makes a scrubber feel attached to the model.
        this.stepMixer(0);
    }

    /** Where the playhead is and how long the clip runs, for the scrubber. Zeroes when idle. */
    animationProgress(): { time: number; duration: number; running: boolean } {
        if (this.active.action === null) {
            return { time: 0, duration: 0, running: false };
        }

        return {
            time: this.active.action.time,
            duration: this.active.action.getClip().duration,
            running:
                this.active.action.isRunning() && !this.active.action.paused,
        };
    }

    /**
     * The bone palette for one skinned mesh, in the space the Alamo skinned shaders work in.
     *
     * WORLD space, not mesh-local. The skinned effects project with VIEWPROJECTION rather than
     * WORLDVIEWPROJECTION - their own comment says "we are working in world space here" - so the
     * skin matrix has to carry the whole way out. Conjugating only by the bind matrices, the way
     * three's own skinning shader does, leaves the result mesh-local and the turret's barrel flew
     * off; the extra `matrixWorld` is what the difference in projection matrix demands.
     */
    skinPalette(mesh: THREE.SkinnedMesh): Float32Array {
        mesh.skeleton.update();

        // Null until the skeleton has been initialised, which `update` above has just done.
        const source = mesh.skeleton.boneMatrices;
        if (source === null || source === undefined) {
            return this.skinScratch;
        }

        const bones = mesh.skeleton.bones.length;
        const palette =
            this.skinScratch.length === bones * 16
                ? this.skinScratch
                : (this.skinScratch = new Float32Array(bones * 16));

        for (let i = 0; i < bones; i++) {
            SKIN_WORK.fromArray(source, i * 16);
            SKIN_WORK.multiply(mesh.bindMatrix);
            SKIN_WORK.premultiply(mesh.bindMatrixInverse);
            SKIN_WORK.premultiply(mesh.matrixWorld);
            SKIN_WORK.toArray(palette, i * 16);
        }

        return palette;
    }

    /**
     * Advances every PASSIVE subject's clip and applies what it says about visibility.
     *
     * The active subject is not here: its mixer is ticked by the loop and its visibility goes
     * through the ROW CHAIN, which is the thing that describes the model being previewed. A passive
     * subject has no rows, so each of its meshes is drawn by its own switches on every frame - see
     * `PartAssembly.applyOwnFileVisibility`. No bone node is hidden, so a bone the clip hides takes
     * nothing below it with it, and a mesh the file hides is never brought back by the clip.
     */
    advancePassiveSubjects(dt: number): void {
        for (const [subjectId, subject] of this.subjects) {
            const death = subject.death;

            if (
                subjectId === ACTIVE_SUBJECT ||
                death === null ||
                death.destroyed
            ) {
                continue;
            }

            const action = subject.action;

            if (action !== null && subject.mixer !== null) {
                subject.mixer.update(dt);

                const track = subject.visibility.get(action.getClip().name);

                // Emission is what the clip decides for a proxy (`applyEmitterGating`, below); what
                // is already in the air keeps drawing, exactly as it does in the engine.
                for (const part of this.context.assembly.partsOf(subjectId)) {
                    this.context.assembly.applyOwnFileVisibility(part);
                }

                // The same gate the active subject gets, on this subject's own clip. Without it the
                // clip decides only what is SEEN: a burst switched off at this moment goes on
                // burning, and is spent long before the frame that was meant to set it off.
                this.context.particles.applyEmitterGating(
                    subjectId,
                    track,
                    action,
                );
            }

            // A dying object STAYS once its clip is done, on the held frame, until its ending says
            // it is gone - see `deathEnding.ts`. The clip is over when there is none, or it is not
            // a death clip, or it has finished.
            const clipOver =
                action === null ||
                !isDeathClipName(action.getClip().name) ||
                clipDone(action);

            if (stepDeath(death, dt, clipOver)) {
                // Its effects are released FIRST, and they outlive it. The Calamari clone's three
                // `p_explosion_huge00` ignite at 59.7s of a 60.1s clip - the detonation the whole
                // minute builds to - and hiding the part they are attached to cut them 0.4s in, so the
                // end of a death was 719 particles alive and NONE of them on screen.
                this.context.particles.releaseSubjectEffects(subjectId);

                for (const part of this.context.assembly.partsOf(subjectId)) {
                    this.context.assembly.setPartHidden(part.id, true);
                }

                this.deathEnds.get(subjectId)?.();
            }
        }
    }

    /**
     * Switches bones on and off for the frame the playing clip is on.
     *
     * Only the nodes this pass hides are touched, and every one of them is released the moment the
     * clip stops naming it - so a bone hidden by one clip cannot stay hidden into the next, and a
     * bone the reader hid by hand in the tree is never quietly shown again by an animation ending.
     */
    applyAnimatedVisibility(): void {
        // Looked up ONCE: this runs on every frame of a playing clip, and so does the emitter
        // gating below, which asks per particle system.
        const active = this.active;
        const track =
            active.action === null
                ? undefined
                : active.visibility.get(active.action.getClip().name);

        if (track !== undefined && active.action !== null) {
            // Through the ROW CHAIN, which is the only thing that writes visibility - the clip is
            // one of a part's four switches there (`animation`). This used to write
            // `node.visible` here instead, straight past the chain, and the two disagreed about how
            // a track is keyed: tracks by glTF NODE name, the chain by BONE name. Whichever of them
            // matched was the one that worked, and re-keying the tracks silently swapped which.
            //
            // The symptom that gave it away: with the per-frame writer missing its lookup, the only
            // resolve was the one `play` does at frame 0 - so a muzzle whose track began `1111` sat
            // on for the whole clip and the one beginning `0000` never lit.
            this.context.rows.resolveRows();
            this.overridingVisibility = true;
        } else if (this.overridingVisibility) {
            this.overridingVisibility = false;
            this.context.levels.applyLevels();
        }

        this.context.particles.applyEmitterGating(
            ACTIVE_SUBJECT,
            track,
            active.action,
        );
    }

    /** One subject's animation state, created the first time it is asked for. */
    subjectAnimation(subjectId: string): SubjectAnimation {
        const held = this.subjects.get(subjectId);

        if (held !== undefined) {
            return held;
        }

        const made: SubjectAnimation = {
            mixer: null,
            clips: [],
            visibility: new Map(),
            nodeByName: new Map(),
            restPose: new Map(),
            action: null,
            death: null,
        };

        this.subjects.set(subjectId, made);

        return made;
    }
}
