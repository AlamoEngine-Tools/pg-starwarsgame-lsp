// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The host's messages: a new scene resets the panel and restores what the reader left, and every
// part, system, texture and reply after it lands on the viewport and the state that reads it. The
// listener is made once and posts 'ready', so everything it touches arrives as a setter or a ref.

import * as l10n from "@vscode/l10n";
import {
    useEffect,
    type Dispatch,
    type SetStateAction,
    type RefObject,
} from "react";
import {
    type PreviewBreakoffProp,
    type ModelDetail,
    PREVIEW_SCENE_KIND,
    type PreviewProblem,
    type PreviewScene,
    type AlamoParticleContent,
    type PreviewParticle,
} from "../../protocol/modelPreview";
import { type ModelPreviewToWebview } from "../../protocol/panels/modelPreview";
import { isSubjectSystem, type LoadTally } from "./loadProgress";
import { particleNotices } from "./particleNotices";
import { heldModels, post } from "./previewPost";
import { BREAKOFF_PART, DEATH_CLONE_PART, WRECK_EFFECT } from "./partIds";
import { dispatchHostMessage } from "./hostMessages";
import { type PendingEffect } from "./useParticleEffects";
import { BREAKOFF_ATTACHMENT, type PreviewViewport } from "./viewport";
import { type PreviewAssetLoader } from "./previewAssetLoader";
import { defaultMode, type PreviewMode } from "./previewMode";
import { attackerFromProjectile } from "./attacker";
import { type BreakoffAnchor } from "./breakoff";
import { type SubjectState, subjectStateFrom } from "../../shared/subjectState";
import {
    viewerSettingsFrom,
    type ViewerSettings,
} from "../../shared/viewerSettings";
import {
    projectSettingsFrom,
    type Attacker,
    type AttackerPreset,
} from "../../shared/projectSettings";
import { transport } from "../shared/vscodeApi";

/** What useHostMessages reads from the rest of the preview. */
export interface HostMessagesInputs {
    applyBoundCamera: (viewport: PreviewViewport) => boolean;
    applyRoom: (room: ViewerSettings) => void;
    assets: PreviewAssetLoader;
    attachPending: () => void;
    breakoffsRef: RefObject<
        { id: string; prop: PreviewBreakoffProp; at: BreakoffAnchor }[]
    >;
    deadRef: RefObject<boolean>;
    detailWantedRef: RefObject<string | null>;
    explosionsRef: RefObject<PendingEffect[]>;
    foldedRef: RefObject<boolean>;
    lodChosenRef: RefObject<boolean>;
    passiveEffectIdsRef: RefObject<Set<string>>;
    pendingRef: RefObject<PreviewParticle[]>;
    playEffect: (
        id: string,
        name: string,
        partId: string | undefined,
        bone: string | undefined,
        once: boolean,
        at?: { x: number; y: number; z: number },
    ) => void;
    queuePassiveEffects: (
        particles: readonly PreviewParticle[],
        partId: string,
    ) => void;
    refreshStats: () => void;
    refreshingRef: RefObject<boolean>;
    requestMaterialAssets: (viewport: PreviewViewport) => void;
    requestedSystemsRef: RefObject<Set<string>>;
    restoredRef: RefObject<boolean>;
    sceneRef: RefObject<PreviewScene | null>;
    setActiveAbilities: Dispatch<SetStateAction<ReadonlySet<string>>>;
    setAlt: Dispatch<SetStateAction<number>>;
    setAnimation: Dispatch<SetStateAction<string | null>>;
    setAttachmentIssues: Dispatch<SetStateAction<string[]>>;
    setAttacker: Dispatch<SetStateAction<Attacker>>;
    setAttackerPresets: Dispatch<SetStateAction<AttackerPreset[]>>;
    setAttackerProjectile: Dispatch<SetStateAction<string | null>>;
    setBoneFilter: Dispatch<SetStateAction<string>>;
    setCollapsed: Dispatch<SetStateAction<ReadonlySet<string>>>;
    setCustomColour: Dispatch<SetStateAction<string | null>>;
    setDestroyed: Dispatch<SetStateAction<ReadonlySet<string>>>;
    setDetailsRow: Dispatch<SetStateAction<string | null>>;
    setEmitters: Dispatch<SetStateAction<string[]>>;
    setEnergyPool: Dispatch<SetStateAction<boolean>>;
    setFaction: Dispatch<SetStateAction<string>>;
    setHasWeather: Dispatch<SetStateAction<boolean>>;
    setHiddenEmitters: Dispatch<SetStateAction<ReadonlySet<number>>>;
    setHiddenWeapons: Dispatch<SetStateAction<ReadonlySet<string>>>;
    setLod: Dispatch<SetStateAction<number>>;
    setMode: Dispatch<SetStateAction<PreviewMode>>;
    setModelDetail: Dispatch<SetStateAction<ModelDetail | null>>;
    setProblems: Dispatch<SetStateAction<PreviewProblem[]>>;
    setProjectileNote: Dispatch<SetStateAction<string | null>>;
    setProjectileSearch: Dispatch<SetStateAction<string>>;
    setScene: Dispatch<SetStateAction<PreviewScene | null>>;
    setSweeping: Dispatch<SetStateAction<ReadonlySet<string>>>;
    setTally: Dispatch<SetStateAction<LoadTally>>;
    storedCustomColourRef: RefObject<string | null>;
    storedFactionRef: RefObject<string | null>;
    storedSubjectRef: RefObject<SubjectState | null>;
    systemScalesRef: RefObject<Map<string, number>>;
    systemsRef: RefObject<Map<string, AlamoParticleContent>>;
    viewportRef: RefObject<PreviewViewport | null>;
}

export function useHostMessages({
    applyBoundCamera,
    applyRoom,
    assets,
    attachPending,
    breakoffsRef,
    deadRef,
    detailWantedRef,
    explosionsRef,
    foldedRef,
    lodChosenRef,
    passiveEffectIdsRef,
    pendingRef,
    playEffect,
    queuePassiveEffects,
    refreshStats,
    refreshingRef,
    requestMaterialAssets,
    requestedSystemsRef,
    restoredRef,
    sceneRef,
    setActiveAbilities,
    setAlt,
    setAnimation,
    setAttachmentIssues,
    setAttacker,
    setAttackerPresets,
    setAttackerProjectile,
    setBoneFilter,
    setCollapsed,
    setCustomColour,
    setDestroyed,
    setDetailsRow,
    setEmitters,
    setEnergyPool,
    setFaction,
    setHasWeather,
    setHiddenEmitters,
    setHiddenWeapons,
    setLod,
    setMode,
    setModelDetail,
    setProblems,
    setProjectileNote,
    setProjectileSearch,
    setScene,
    setSweeping,
    setTally,
    storedCustomColourRef,
    storedFactionRef,
    storedSubjectRef,
    systemScalesRef,
    systemsRef,
    viewportRef,
}: HostMessagesInputs) {
    // ── messages from the host ────────────────────────────────────────────────
    useEffect(() => {
        const onMessage = async (
            message: ModelPreviewToWebview,
        ): Promise<void> => {
            const viewport = viewportRef.current;
            if (viewport === null) {
                return;
            }

            await dispatchHostMessage(message, {
                scene: (message) => {
                    refreshingRef.current = message.refresh === true;
                    viewport.clear();
                    setScene(message.scene);
                    setProblems(message.scene.problems);
                    setAnimation(null);
                    setBoneFilter("");
                    setCollapsed(new Set());
                    setEmitters([]);
                    setHiddenEmitters(new Set());
                    setHiddenWeapons(new Set());
                    setActiveAbilities(new Set());
                    setProjectileSearch("");
                    setProjectileNote(null);
                    setSweeping(new Set());
                    setAlt(0);
                    setLod(0);
                    lodChosenRef.current = false;
                    setDestroyed(new Set());

                    // The faction the reader last picked, if THIS subject has one by that name -
                    // reviewing a roster should survive opening the next unit, and fall away quietly
                    // when it does not apply rather than tinting the wrong thing.
                    setFaction(
                        message.scene.factions.some(
                            (f) => f.name === storedFactionRef.current,
                        )
                            ? (storedFactionRef.current ?? "")
                            : "",
                    );

                    // The custom colour survives too, and used to be thrown away here on every subject.
                    // It needs no "if this subject has it" guard: a hex is not a name out of the
                    // roster, so it applies to anything. Storing it was dead code while this stood -
                    // written on every save, read back on open, and nulled by the next scene message.
                    setCustomColour(storedCustomColourRef.current);
                    foldedRef.current = false;
                    setMode(
                        defaultMode(message.scene.kind, {
                            animations: 0,
                            hardpoints: message.scene.hardpoints.length,
                            particles: message.scene.particles.length,
                            weapons: message.scene.weapons.length,
                            abilities: message.scene.abilities?.length ?? 0,
                        }),
                    );

                    // Where this subject was left earlier in the session. Applied AFTER the reset above,
                    // which is what the reset is for on a subject opened for the first time - the
                    // opening rules - and would otherwise undo.
                    const previous = storedSubjectRef.current;

                    if (previous !== null) {
                        setDestroyed(new Set(previous.destroyed));
                        setHiddenEmitters(new Set(previous.hiddenEmitters));
                        setAnimation(previous.animation);
                        setBoneFilter(previous.filterText);
                        setCollapsed(new Set(previous.collapsed));
                        foldedRef.current = true;
                    }
                    systemsRef.current.clear();
                    systemScalesRef.current.clear();
                    requestedSystemsRef.current.clear();
                    assets.clearLedgers();

                    // The scene on screen is what is kept: a model this scene has no part for is let
                    // go here. An XML edit can drop a hardpoint and the model it attached.
                    heldModels.retain(
                        message.scene.parts
                            .filter((part) => part.resolved)
                            .map((part) => ({ modelReference: part.modelRef })),
                    );

                    // Cover the viewport again for the new subject. The expected count is the parts
                    // that RESOLVED, because those are the only ones a GLB is asked for - counting the
                    // rest would leave the cover waiting on geometry nobody requested.
                    setTally({
                        expectedParts: message.scene.parts.filter(
                            (part) => part.resolved,
                        ).length,
                        arrivedParts: 0,
                        requestedAssets: 0,
                        settledAssets: 0,
                        timedOut: false,
                    });
                    // Defensive: a server older than this client sends no particles at all, and
                    // spreading undefined throws inside the handler - which loses the whole scene,
                    // geometry included, for the sake of an effects list.
                    const particles = message.scene.particles ?? [];
                    pendingRef.current = [...particles];
                    // The old subject's wrecks are gone with it, and so are the ids they were keyed by.
                    passiveEffectIdsRef.current.clear();

                    refreshStats();

                    // One request per distinct system, however many proxies call for it.
                    for (const name of new Set(
                        particles.map((particle) => particle.systemRef),
                    )) {
                        requestedSystemsRef.current.add(name.toLowerCase());
                        post({ type: "requestParticleSystem", name });
                    }

                    // A particle system is not geometry, so it takes getParticleSystem rather than
                    // getModelGlb. The server has already classified the file by its root chunk; the
                    // extension alone cannot tell the two apart.
                    if (message.scene.kind === PREVIEW_SCENE_KIND.particle) {
                        post({
                            type: "requestParticleSystem",
                            name: message.scene.subject,
                        });
                        return;
                    }

                    // What is inside the HULL, for the inspector. Only the hull: an attached turret is a
                    // model of its own with its own bone list, and mixing two files' indices into one
                    // answer is exactly the join mistake `alamoMeshIndex` exists to prevent.
                    const hull =
                        message.scene.parts.find((part) => part.resolved) ??
                        null;
                    detailWantedRef.current = hull?.modelRef ?? null;

                    // Kept from the scene before when the hull's file did not change: the detail is
                    // read off the model's file, and reading a 149 MB one again took 0.1 to 0.6 s of
                    // every refresh. Asked for again below if the hull's bytes turn out to be new.
                    const keptDetail =
                        hull === null
                            ? null
                            : heldModels.detailOf(hull.modelRef);
                    setModelDetail(keptDetail);

                    if (hull !== null && keptDetail === null) {
                        post({
                            type: "requestModelDetail",
                            modelReference: hull.modelRef,
                        });
                    }

                    // Ask for every part's geometry separately, so a large unit draws its hull as soon as
                    // it arrives rather than after the last turret.
                    for (const part of message.scene.parts) {
                        if (!part.resolved) {
                            continue;
                        }

                        post({
                            type: "requestGlb",
                            partId: part.id,
                            modelReference: part.modelRef,
                            attachToPartId: part.attachToPartId ?? undefined,
                            attachBone: part.attachBone ?? undefined,
                        });
                    }
                },
                glb: async (message) => {
                    // The bytes this reply stands for: the ones it carries, or the ones kept from
                    // the scene before when the server says the model did not change.
                    const taken = heldModels.take(message);
                    const glb = taken?.bytes ?? null;

                    if (glb === null) {
                        const error = message.error ?? null;
                        if (error !== null) {
                            setProblems((current) => [
                                ...current,
                                { severity: "error", message: error },
                            ]);
                        }

                        // A part that failed is a part that is not coming. It still has to be counted
                        // or the cover would sit over the rest of the model until the wait timed out -
                        // and one unreadable turret is exactly the case where a reader most wants to
                        // see what DID load.
                        if (
                            !message.partId.startsWith(BREAKOFF_PART) &&
                            !message.partId.startsWith(DEATH_CLONE_PART)
                        ) {
                            setTally((current) => ({
                                ...current,
                                arrivedParts: current.arrivedParts + 1,
                            }));
                        }

                        return;
                    }

                    // Wreckage waiting on this model, if any. Handled BEFORE the part path, because a
                    // breakoff GLB carries no partId and would otherwise be added as a scene part
                    // called `undefined` and framed as if it were the subject.
                    if (message.partId.startsWith(BREAKOFF_PART)) {
                        const key = message.partId.slice(BREAKOFF_PART.length);
                        const pending = breakoffsRef.current.find(
                            (entry) => entry.id === key,
                        );

                        breakoffsRef.current = breakoffsRef.current.filter(
                            (entry) => entry.id !== key,
                        );

                        if (pending !== undefined) {
                            await viewport.addBreakoff(
                                pending.id,
                                glb,
                                pending.at,
                                pending.prop,
                            );

                            // The fire the piece trails. Attached to the WRECK, not to the hardpoint it
                            // came off: 86 of the 90 shipped props name one, and every one of them was
                            // being ignored, so debris tumbled away cold.
                            playEffect(
                                `${WRECK_EFFECT}${pending.id}`,
                                pending.prop.attachedParticle ?? "",
                                `${BREAKOFF_ATTACHMENT}${pending.id}`,
                                undefined,
                                false,
                            );

                            // And the effects the prop's MODEL carries, which are a separate thing
                            // from `Debris_Attached_Particle` above - the XML names one, the ALO
                            // carries however many the artist pinned to its bones.
                            queuePassiveEffects(
                                pending.prop.particles ?? [],
                                `${BREAKOFF_ATTACHMENT}${pending.id}`,
                            );

                            // Debris needs its textures and its shaders like any other geometry. The
                            // first build returned here instead, so a wreck was drawn on a bare
                            // archetype material with no map bound at all - a black shape tumbling out
                            // of the gap, which is exactly how it looked. A prop model is usually a
                            // piece of the hull it fell off, so most of these are already decoded and
                            // arrive back immediately.
                            requestMaterialAssets(viewport);
                        }

                        return;
                    }

                    // The wreck the ship leaves behind. Loaded as its own part; the hull it replaces
                    // was already hidden by the death watch that asked for this, since the ship dies
                    // whether or not a clone answers.
                    if (message.partId.startsWith(DEATH_CLONE_PART)) {
                        // Its own PASSIVE SUBJECT - a wreck is a replacement subject, not another piece
                        // of the ship being previewed, and the active subject's hardpoint answers must
                        // not reach it by mesh name.
                        await viewport.addPart(
                            message.partId,
                            glb,
                            undefined,
                            undefined,
                            {
                                subjectId: message.partId,
                                // Its OWN scale: a clone is spawned as its own object, so it is not drawn
                                // at the ship's. 59 of 265 shipped pairs disagree.
                                scale:
                                    sceneRef.current?.deathClones?.find(
                                        (c) =>
                                            `${DEATH_CLONE_PART}${c.objectId}` ===
                                            message.partId,
                                    )?.scaleFactor ?? 1,
                            },
                        );

                        // The explosions, the fire smoke and the debris trails the clone's own model
                        // carries - eight of them on the Star Destroyer's wreck, and not one reached
                        // the client until its descriptor carried them.
                        queuePassiveEffects(
                            sceneRef.current?.deathClones?.find(
                                (c) =>
                                    `${DEATH_CLONE_PART}${c.objectId}` ===
                                    message.partId,
                            )?.particles ?? [],
                            message.partId,
                        );

                        requestMaterialAssets(viewport);

                        // FETCHED EARLY, held hidden. Asking for it at the moment of death left a
                        // visible gap between the ship going and the wreck arriving - a capital ship's
                        // clone is three megabytes. Shown and started by the death watch.
                        if (deadRef.current) {
                            viewport.startDeathClip(message.partId);
                        } else {
                            viewport.setPartHidden(message.partId, true);
                        }

                        return;
                    }

                    await viewport.addPart(
                        message.partId,
                        glb,
                        message.attachToPartId ?? undefined,
                        message.attachBone ?? undefined,
                    );

                    // The hull's file changed under a detail kept from before: read it again.
                    if (
                        taken?.replaced === true &&
                        taken.modelReference === detailWantedRef.current
                    ) {
                        post({
                            type: "requestModelDetail",
                            modelReference: taken.modelReference,
                        });
                    }

                    // Counted here rather than at every `glb`: a wreck, a death clone and a breakoff
                    // prop all arrive down this same message and have already returned above. Only a
                    // scene part is one of the parts the cover is waiting for.
                    setTally((current) => ({
                        ...current,
                        arrivedParts: current.arrivedParts + 1,
                    }));

                    // The bound shot, if this subject has one, INSTEAD of the default framing - not
                    // after it, so the model does not visibly jump from one to the other on open.
                    // Always overridable by hand afterwards: a rule is a default, not a cage.
                    //
                    // Skipped entirely on a refresh. Framing is an OPENING rule: it answers "I have
                    // never seen this before, show me all of it". A reader who edited a file while
                    // looking closely at a turret has already answered that question, and re-framing
                    // would throw the answer away every time they saved.
                    if (!refreshingRef.current && !applyBoundCamera(viewport)) {
                        viewport.frameAll();
                    }

                    refreshStats();

                    requestMaterialAssets(viewport);

                    // This part's bones now exist, so anything waiting to attach to them can.
                    attachPending();

                    // And whatever STILL has nowhere to hang. `attachmentFor` draws a bone it cannot
                    // find at the owning model's origin and says nothing, which hid two separate
                    // misplacement bugs.
                    //
                    // REPLACED, not appended. The list is recomputed per arriving part and a part that
                    // lands later does drop off it - but the panel was accumulating every answer it
                    // had ever been given, so a subject whose parts arrive one at a time reported all
                    // the ones that had not arrived YET and never took them back. Measured on the
                    // Executor, whose 30 parts produced 70 warnings about parts that were all present
                    // by the time anyone read them, against a live count of zero.
                    setAttachmentIssues(viewport.unresolvedAttachments());
                },
                modelDetail: (message) => {
                    // Only when it is still about the subject on screen. Replies cross with a change of
                    // model, and showing one model's bone matrices against another is worse than
                    // showing none - the numbers would look perfectly plausible.
                    //
                    // Matched on the ECHOED request, not on `detail.model`: the server normalises a
                    // reference (`EV_StarDestroyer.ALO` comes back as `Ev_stardestroyer`), so comparing
                    // against what was asked for would never match and the panel would stay empty.
                    const arrived = message.result.detail ?? null;

                    if (arrived !== null) {
                        heldModels.keepDetail(message.modelReference, arrived);
                    }

                    if (
                        arrived !== null &&
                        message.modelReference === detailWantedRef.current
                    ) {
                        setModelDetail(arrived);
                    }
                },
                // The inspector tab was closed, so no row is being inspected any more - the mark on
                // the row it was showing comes off. Told rather than inferred: the tab is its own
                // editor and the reader can close it from the tab bar, which this side cannot see.
                inspectorClosed: (message) => {
                    setDetailsRow(null);
                },
                particleSystem: (message) => {
                    // A particle scene's one part is its own system, asked for here rather than as a
                    // GLB - so it is counted in here, or the cover waits on geometry that never comes.
                    // Before the null check: a system that failed to read is still an answer.
                    if (
                        sceneRef.current !== null &&
                        isSubjectSystem(sceneRef.current, message.name)
                    ) {
                        setTally((current) => ({
                            ...current,
                            arrivedParts: current.arrivedParts + 1,
                        }));
                    }

                    const system = message.result.system ?? null;

                    if (system === null) {
                        const error = message.result.error ?? null;
                        if (error !== null) {
                            setProblems((current) => [
                                ...current,
                                { severity: "error", message: error },
                            ]);
                        }
                        return;
                    }

                    // What the file holds that the game does not apply: skipped as the game skips it,
                    // and said once per system, whichever way the system was asked for.
                    const skipped = particleNotices(
                        message.name,
                        message.result,
                    );
                    if (skipped.length > 0) {
                        setProblems((current) => [...current, ...skipped]);
                    }

                    // A particle file opened directly is its own subject and attaches to nothing.
                    //
                    // Anything WAITING on this system says it is not that: a death explosion and a
                    // wreck's trailing fire are both asked for by name at the moment they are needed,
                    // and neither is in the scene's proxy list. Without the third test they took this
                    // branch - so the blast was parented to the scene root instead of to the thing
                    // that blew up, and it called `frameAll`, yanking the camera off the subject.
                    const awaited = explosionsRef.current.some(
                        (pending) =>
                            pending.system.toLowerCase() ===
                            message.name.toLowerCase(),
                    );

                    if (
                        !awaited &&
                        pendingRef.current.length === 0 &&
                        !requestedSystemsRef.current.has(
                            message.name.toLowerCase(),
                        )
                    ) {
                        // A particle FILE opened on its own IS the subject, so it is model-owned - and
                        // it has no hull, so it reaches no model tree either way.
                        viewport.addParticleSystem(message.name, system, {
                            origin: "model",
                        });
                        viewport.frameAll();

                        // Heat emitters are labelled rather than hidden: they draw as a distortion of
                        // the frame behind them, which is easy to miss and easy to mistake for a bug
                        // in the effect if the row does not say what it is.
                        setHasWeather(viewport.hasWeather());
                        setEmitters(
                            system.emitters.map(
                                (emitter) =>
                                    emitter.name +
                                    (emitter.properties.isHeatParticle
                                        ? " [heat]"
                                        : ""),
                            ),
                        );

                        // Asked for only now, because the system is what names them - the effect it
                        // is drawn with as much as its textures.
                        for (const name of viewport.particleTextureNames()) {
                            assets.requestTexture(name);
                        }
                        for (const shader of viewport.shaderNames()) {
                            assets.requestShader(shader);
                        }

                        return;
                    }

                    systemsRef.current.set(message.name.toLowerCase(), system);
                    systemScalesRef.current.set(
                        message.name.toLowerCase(),
                        message.result.scaleFactor ?? 1,
                    );

                    // A death explosion asked for at the moment of destruction plays as soon as it lands.
                    const waiting = explosionsRef.current.filter(
                        (pending) =>
                            pending.system.toLowerCase() ===
                            message.name.toLowerCase(),
                    );

                    if (waiting.length > 0) {
                        explosionsRef.current = explosionsRef.current.filter(
                            (pending) => !waiting.includes(pending),
                        );

                        // The scale that arrived with THIS system, not a lookup: an effect asked for
                        // before its system landed takes the same route as one asked for after, and
                        // the two disagreeing is exactly how a wreck's fire came back unscaled.
                        const scale = message.result.scaleFactor ?? 1;

                        for (const pending of waiting) {
                            if (pending.once) {
                                viewport.playOnce(
                                    pending.id,
                                    system,
                                    pending.partId,
                                    pending.bone,
                                    scale,
                                    pending.at,
                                );
                            } else {
                                viewport.addParticleSystem(pending.id, system, {
                                    origin: "gameplay",
                                    attachToPartId: pending.partId,
                                    attachBone: pending.bone,
                                    scaleFactor: scale,
                                    at: pending.at,
                                });
                            }
                        }
                    }

                    attachPending();
                },
                previewFeatures: (message) => {
                    // Energy DEFAULTS OFF and every read goes through this, so a panel that never
                    // receives the message - an older host, or one lost on startup - stays in the state
                    // the shipped game is in rather than offering a mechanic no player will ever see.
                    const features = message.features as
                        { energyPool?: unknown } | null | undefined;

                    setEnergyPool(features?.energyPool === true);
                },
                viewerSettings: (message) => {
                    // The room the reader left behind. Applied before any geometry arrives, so nothing
                    // visibly snaps into place a frame later.
                    const room = viewerSettingsFrom(message.settings);

                    // And what THIS PROJECT remembers, which travels beside the room rather than
                    // inside it - the two are at different tiers. See `projectSettings.ts`.
                    const project = projectSettingsFrom(message.project);

                    setAttacker(project.attacker);
                    setAttackerPresets(project.presets);
                    setCustomColour(project.customColour);

                    // By NAME, and only if this subject has it: "I am reviewing the Rebel roster"
                    // should survive the next unit and fall away quietly when it does not apply.
                    storedFactionRef.current = project.faction;
                    storedCustomColourRef.current = project.customColour;

                    applyRoom(room);

                    storedSubjectRef.current =
                        message.subject === null
                            ? null
                            : subjectStateFrom(message.subject);

                    restoredRef.current = true;
                },
                projectile: (message) => {
                    // The answer to a "fill from a projectile" pick the scene could not resolve.
                    //
                    // Applied through `attackerFromProjectile`, the SAME reader the resolved ones go
                    // through, so a bolt fetched on demand and one that came with the scene fill the
                    // panel identically. The bolt itself is not kept in `scene.projectiles`: the fill is
                    // a copy and the reader's edits afterwards have to stick.
                    const bolt = message.result.projectile ?? null;

                    if (bolt === null) {
                        setProjectileNote(
                            message.result.error ??
                                l10n.t("{0} could not be read.", message.name),
                        );
                    } else {
                        setAttacker((current) =>
                            attackerFromProjectile(bolt, current),
                        );
                        setAttackerProjectile(bolt.id);
                        setProjectileNote(null);
                    }
                },
                shader: (message) => {
                    assets.receiveShader(
                        message.name,
                        message.result.source ?? null,
                    );
                },
                texture: (message) => {
                    assets.receiveTexture(message.name, message);
                },
            });
        };

        // Wrapped: the handler is async, and its rejection must not go unhandled.
        const stop = transport.listen((data) => {
            void onMessage(data as ModelPreviewToWebview);
        });
        post({ type: "ready" });

        return stop;
        // Deliberately NOT depending on `scene`: this effect posts 'ready', so re-running it asks
        // for another scene, which changes `scene`, which re-runs it. The handler reads the current
        // scene through a ref instead.
    }, [attachPending, refreshStats]);

    return {};
}
