// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The viewport's animation: each subject's mixer and clips kept apart, the clip playing with its speed,
// loop and pause, seeking and the rest pose, a death clip, the passive subjects stepped on their own, the
// bone visibility a clip drives, and the skin palette built per frame. It reads the rows, the particle
// systems and the assembly; nothing else.

import { type SubjectAnimation } from "./subjectAnimation";
import { ACTIVE_SUBJECT } from "./loadedPart";
import { deathClip } from "./visibility";
import * as THREE from "three";
import { hiddenAt, visibilityTracks } from "./boneVisibility";
import { type MaterialExtras, resolveMaterial } from "./materials";
import { alamoNodeName } from "./skeleton";
import { type GLTF } from "three/examples/jsm/loaders/GLTFLoader.js";
import { type ViewportContext } from "./viewportContext";

/** Scratch for the bone-palette maths, so a per-frame loop allocates nothing. */
const SKIN_WORK = new THREE.Matrix4();

/** Every mesh a model's own materials say is not drawn - collision hulls, shadow volumes. */
function hiddenMeshes(root: THREE.Object3D): Set<THREE.Object3D> {
    const hidden = new Set<THREE.Object3D>();

    root.traverse((node) => {
        if (
            node instanceof THREE.Mesh &&
            resolveMaterial((node.userData.alamo ?? {}) as MaterialExtras)
                .hidden
        ) {
            hidden.add(node);
        }
    });

    return hidden;
}

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

        // What its own file says is not drawn - a collision hull, a shadow volume. Held so a clip
        // can never bring one back: a shadow volume is not geometry that happens to be switched off
        // this frame.
        for (const mesh of hiddenMeshes(root)) {
            subject.fileHidden.add(mesh);
        }

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
            subject.fileHidden.delete(node);

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
    }

    /** Steps the active subject's clip on by `dt` seconds. */
    advance(dt: number): void {
        this.active.mixer?.update(dt);
    }

    /** Drops every subject's animation and the visibility a clip was driving, for a new scene. */
    forgetSubjects(): void {
        this.subjects.clear();
        this.overridingVisibility = false;
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
     * Starts a passive subject's own death clip, once.
     *
     * ONCE, and clamped: the user's word is that a death clone is spawned at the exact position of
     * the model it replaces and plays through a single time. There is no picker for it and no loop
     * - it is not something the reader chose, it is what dying looks like.
     *
     * Separate from LOADING it so the geometry can be fetched early and held: asking for a capital
     * ship's three-megabyte clone at the moment of death left a visible gap between the ship going
     * and the wreck arriving. Idempotent - a second call does not restart a clip already running,
     * because it is the same wreck.
     *
     * This is `play` for a subject nobody is driving. It is the same machinery: the subject's own
     * mixer, the subject's own clips, the subject's own tracks. It used to be a second
     * implementation of all three, because the scene-wide maps it would have had to use were
     * already full of the hull's answers.
     */
    startDeathClip(subjectId: string): void {
        const subject = this.subjects.get(subjectId);

        if (
            subject === undefined ||
            subject.mixer === null ||
            subject.action !== null
        ) {
            return;
        }

        const name = deathClip(subject.clips.map((c) => c.name));
        const clip = subject.clips.find((c) => c.name === name);

        if (clip === undefined) {
            return;
        }

        subject.action = subject.mixer.clipAction(clip);
        subject.action.setLoop(THREE.LoopOnce, 1);
        // Without this the last pose is gone the frame it arrives.
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

        this.active.action = this.active.mixer.clipAction(clip);
        this.active.action.setLoop(
            this.animationLoop ? THREE.LoopRepeat : THREE.LoopOnce,
            Infinity,
        );
        // Without this a non-looping action snaps back to frame 0 the instant it ends, so the last
        // pose - often the whole point of a death or a deploy - is never on screen.
        this.active.action.clampWhenFinished = true;
        this.active.action.timeScale = this.animationSpeed;
        this.active.action.paused = this.animationPaused;
        this.active.action.play();

        return true;
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

        if (this.active.action !== null) {
            this.active.action.setLoop(
                loop ? THREE.LoopRepeat : THREE.LoopOnce,
                Infinity,
            );
            this.active.action.clampWhenFinished = true;
        }
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
        this.active.mixer?.update(0);
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
     * subject has no rows, so its track is applied to its own nodes directly - safe now that
     * `nodeByName` is the subject's own rather than a scene-wide map the hull had filled.
     *
     * A mesh the FILE hides is never brought back by the clip: a shadow volume is not geometry that
     * happens to be switched off this frame.
     */
    advancePassiveSubjects(dt: number): void {
        for (const [subjectId, subject] of this.subjects) {
            if (
                subjectId === ACTIVE_SUBJECT ||
                subject.action === null ||
                subject.mixer === null
            ) {
                continue;
            }

            subject.mixer.update(dt);

            const action = subject.action;
            const track = subject.visibility.get(action.getClip().name);

            if (track !== undefined) {
                // Every bone the track names, proxies included: a proxy carries only the marker
                // mesh the file already hides, and no effect is attached to it to be taken with it.
                // Emission is what the clip decides for a proxy (`applyEmitterGating`); what is
                // already in the air keeps drawing, exactly as it does in the engine.
                //
                // Through the subject's own name map rather than a traversal of its whole root:
                // this runs every frame, and a capital ship's wreck is a hundred nodes.
                for (const [name, bits] of track.bones) {
                    const node = subject.nodeByName.get(name);

                    if (node !== undefined && !subject.fileHidden.has(node)) {
                        node.visible = !hiddenAt(bits, track.fps, action.time);
                    }
                }
            }

            // The same gate the active subject gets, on this subject's own clip. Without it the
            // clip decides only what is SEEN: a burst switched off at this moment goes on burning,
            // and is spent long before the frame that was meant to set it off.
            this.context.particles.applyEmitterGating(subjectId, track, action);

            // Gone at the end of it. A death clone is what dying LOOKS like, not what is left
            // afterwards - the engine takes the object away when the clip is done.
            if (action.time >= action.getClip().duration) {
                // Its effects are released FIRST, and they outlive it. The Calamari clone's three
                // `p_explosion_huge00` ignite at 59.7s of a 60.1s clip - the detonation the whole
                // minute builds to - and hiding the part they are attached to cut them 0.4s in, so the
                // end of a death was 719 particles alive and NONE of them on screen.
                this.context.particles.releaseSubjectEffects(subjectId);

                for (const part of this.context.assembly.partsOf(subjectId)) {
                    this.context.assembly.setPartHidden(part.id, true);
                }
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
            // Through the ROW CHAIN, which is the only thing that writes visibility - the clip is a
            // link in it (`animated`), above the level gates and the file. This used to write
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
            fileHidden: new Set(),
        };

        this.subjects.set(subjectId, made);

        return made;
    }
}
