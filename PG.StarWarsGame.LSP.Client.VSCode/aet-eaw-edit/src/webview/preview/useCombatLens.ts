// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The combat lens: what the subject has left and what has been done to it. The attacker built to
// shoot it, its pools and hull, the hardpoints destroyed, the damage log, the death and the wreck, and
// the weapons and cards the Hardpoints and Unit weapons sections draw. One hook so those panels read
// one object rather than thirty loose names.

import * as l10n from "@vscode/l10n";
import {
    useCallback,
    useEffect,
    useMemo,
    useRef,
    useState,
    type Dispatch,
    type SetStateAction,
    type RefObject,
} from "react";
import {
    type PreviewBreakoffProp,
    type PreviewParticle,
    type PreviewScene,
} from "../../protocol/modelPreview";
import { post } from "./previewPost";
import { BREAKOFF_PART, DEATH_CLONE_PART } from "./partIds";
import { type TreeItem } from "./previewTree";
import { type PreviewViewport } from "./viewport";
import { collisionMeshNames, decalNames, partHidden } from "./damage";
import { stageForHull } from "./levels";
import { hardpointCards, unitWeapons } from "./hardpointCards";
import { type PreviewMode } from "./previewMode";
import { allWeaponIds, visibleArcs, weaponRows } from "./weaponRows";
import {
    attackerProjectile as attackerProjectileSpec,
    fireBlast,
    fullPools,
    type Pools,
    poolsFor,
    resolveHitWithWorkings,
} from "./attacker";
import { shieldRevealed, stealthed } from "./abilityRows";
import { effectState } from "./visibility";
import { cloneForDamage, turretSweeps } from "./deathClone";
import { everyTurretHandle, type TurretAim } from "./turretHandles";
import { spinAwayEnd } from "./spinAway";
import { appendShot, BY_HAND, type DamageLogEntry } from "./damageLog";
import { breakoffAnchor, type BreakoffAnchor, breakoffFor } from "./breakoff";
import {
    hardpointPool,
    hullAfterHardpointDeath,
    hullPool,
    shieldGeneratorsDown,
    unitDestroyed,
    unitTargetable,
} from "./unitPool";
import { DEFAULT_HULL_CONSTRAINT, leashed } from "./healthPools";
import { blastVictims, candidatesFrom } from "./blast";
import { useEdgeResize } from "../shared/useEdgeResize";
import { readPanelSize, writePanelSize } from "../../shared/panelLayout";
import { clipNamingModel, readClip } from "./animationNames";
import { isDeployedClip, weaponsInState } from "./deployedState";
import {
    type Attacker,
    type AttackerPreset,
    DEFAULT_ATTACKER,
} from "../../shared/projectSettings";

/** What useCombatLens reads from the rest of the preview. */
export interface CombatLensInputs {
    abilityProxyIds: Map<string, string[]>;
    activeAbilities: ReadonlySet<string>;
    animation: string | null;
    breakoffsRef: RefObject<
        { id: string; prop: PreviewBreakoffProp; at: BreakoffAnchor }[]
    >;
    /** Written here at every render; made by the caller, whose effect callbacks read it first. */
    destroyedRef: RefObject<ReadonlySet<string>>;
    dropPassiveEffects: (partId: string) => void;
    effectDrawsNow: (particle: PreviewParticle) => boolean;
    fireArcs: boolean;
    mode: PreviewMode;
    playEffect: (
        id: string,
        name: string,
        partId: string | undefined,
        bone: string | undefined,
        once: boolean,
        at?: { x: number; y: number; z: number },
    ) => void;
    playhead: { time: number; duration: number; running: boolean };
    scene: PreviewScene | null;
    sceneRef: RefObject<PreviewScene | null>;
    setAlt: Dispatch<SetStateAction<number>>;
    setTreeItems: Dispatch<SetStateAction<TreeItem[]>>;
    viewportRef: RefObject<PreviewViewport | null>;
}

export type CombatLens = ReturnType<typeof useCombatLens>;

export function useCombatLens({
    abilityProxyIds,
    activeAbilities,
    animation,
    breakoffsRef,
    destroyedRef,
    dropPassiveEffects,
    effectDrawsNow,
    fireArcs,
    mode,
    playEffect,
    playhead,
    scene,
    sceneRef,
    setAlt,
    setTreeItems,
    viewportRef,
}: CombatLensInputs) {
    /**
     * What every shot did, newest first.
     *
     * TIER 2 - it describes what has been done to THIS subject, so it clears with the scene. A log
     * carried across subjects would read as damage to a unit that never took any.
     */
    const [damageLog, setDamageLog] = useState<DamageLogEntry[]>([]);

    /**
     * How tall the log flyout stands, remembered across opens.
     *
     * It carries the full arithmetic of every shot now - the armour factor, the shield, the blast
     * division, each pool before and after - so the height that suited a one-line-per-shot list is
     * no longer the height a reader wants. Same hook and same store as the problems panels, so the
     * grip behaves identically wherever it appears.
     */
    const { size: logHeight, handleProps: logResizeN } = useEdgeResize(
        readPanelSize("preview.damageLog", 260),
        120,
        620,
        "n",
        (value) => {
            writePanelSize("preview.damageLog", value);
        },
    );

    // Width as well as height. The lines are long - a factor, both operands and the pool either
    // side of the shot - and a fixed 304px column wrapped nearly every one of them.
    const { size: logWidth, handleProps: logResizeW } = useEdgeResize(
        readPanelSize("preview.damageLog.width", 340),
        280,
        720,
        "w",
        (value) => {
            writePanelSize("preview.damageLog.width", value);
        },
    );
    const [logOpen, setLogOpen] = useState(false);

    /**
     * A moment of red on the Fire button, so a press is visibly a press.
     *
     * The button never latches, and a shot at a full capital ship moves a bar by a percent or two -
     * so without this the only evidence anything happened was a number you had to go looking for.
     */
    const [justFired, setJustFired] = useState(false);

    /* Clears itself. A timer per press rather than a CSS animation, because the flash has to
       restart on a SECOND press - an animation on a class that is already there does not replay,
       so firing twice quickly would have flashed once. */
    useEffect(() => {
        if (!justFired) {
            return;
        }

        const handle = setTimeout(() => setJustFired(false), 180);

        return () => clearTimeout(handle);
    }, [justFired]);
    const logRef = useRef<HTMLDivElement | null>(null);

    /** Hardpoints currently blown off. */
    const [destroyed, setDestroyed] = useState<ReadonlySet<string>>(new Set());

    /**
     * Which hardpoints are swinging, by id.
     *
     * A set rather than the one boolean this was: the button lived on the panel and swung every
     * turret at once, which on a hull whose hardpoints declare different extents is a control that
     * cannot say what it will do. It belongs to the hardpoint that declares the traverse.
     */
    const [sweeping, setSweeping] = useState<ReadonlySet<string>>(new Set());

    /**
     * The weapon the reader has built, and the ones they have saved.
     *
     * Tier 1 - it describes their testing habits, not this model - so both are restored from the
     * stored room and written back by the one effect that owns Tier 1.
     */
    const [attacker, setAttacker] = useState<Attacker>(DEFAULT_ATTACKER);
    const [attackerPresets, setAttackerPresets] = useState<AttackerPreset[]>(
        [],
    );
    const [presetName, setPresetName] = useState("");
    const [projectileSearch, setProjectileSearch] = useState("");

    /** A projectile picked from the catalogue whose values are not on the wire yet. */
    const [projectileNote, setProjectileNote] = useState<string | null>(null);

    /**
     * The projectile the built weapon was filled FROM, if any.
     *
     * Remembered separately from the weapon's own numbers because the blast area belongs to the
     * projectile and has no field in the panel: damage and damage type are things the reader edits,
     * a blast radius is a property of the bolt they picked. Cleared when the subject changes.
     */
    const [attackerProjectile, setAttackerProjectile] = useState<string | null>(
        null,
    );

    /**
     * What is left of the target, and of each hardpoint.
     *
     * Tier 2 at most: a fresh preview opens undamaged, because the opening rules beat persistence.
     * `null` means the scene has said nothing yet.
     */
    const [pools, setPools] = useState<Pools | null>(null);

    /**
     * Whether the energy pool is shown at all.
     *
     * Off until the host says otherwise. The mechanic works in the engine but the shipped game
     * disables it and offers no interface, so a modder turns it on deliberately - see the
     * `aet-eaw-edit.features.preview.energyPool` setting, which says as much.
     */
    const [energyPool, setEnergyPool] = useState(false);

    /**
     * Whether the shield has been shot off the ship rather than shot down.
     *
     * The user's rule: every hardpoint of type shield generator destroyed means no shield at all,
     * whatever the pool holds - the way the engines go out with theirs.
     */
    const shieldsDown = useMemo(
        () => shieldGeneratorsDown(scene?.hardpoints ?? [], destroyed),
        [scene, destroyed],
    );

    /** What the pool readout and the status bars both read. */
    const poolOptions = useMemo(
        () => ({ energy: energyPool, shieldsDown }),
        [energyPool, shieldsDown],
    );
    const [hardpointHealth, setHardpointHealth] = useState<
        Record<string, number | null>
    >({});
    const [fireTarget, setFireTarget] = useState("hull");

    /**
     * Weapon weapons the reader has switched off.
     *
     * The weapons that are OFF rather than the ones that are on, so a subject with a hundred hardpoints
     * opens showing all of them without the panel having to enumerate a hundred ids first - and so
     * the master pill can be flipped without losing which weapons were picked.
     */
    const [hiddenWeapons, setHiddenWeapons] = useState<ReadonlySet<string>>(
        new Set(),
    );

    /**
     * Where the reader has pointed each turret by hand, for the ROW to read out.
     *
     * A copy. The pose itself lives on the bone, in the viewport - see `onTurretAimed` - and this
     * is only what the dock prints beside the extents it is bounded by.
     */
    const [turretAims, setTurretAims] = useState<
        ReadonlyMap<string, TurretAim>
    >(new Map());

    destroyedRef.current = destroyed;

    /**
     * Destroys or repairs one hardpoint.
     *
     * The death explosion fires here rather than in the effect above, because it is an event: it
     * belongs to the moment of destruction, not to the state of being destroyed, and replaying it
     * every time the scene re-renders would leave a ship permanently exploding.
     */
    const setHardpointDestroyed = useCallback(
        (
            id: string,
            isDestroyed: boolean,
            /**
             * Whether to write a line for it.
             *
             * False from the attacker, which logs its own shot with the real numbers - it knows the
             * damage type and how much actually landed, and this cannot. Everything else reaching this
             * is a HAND destroying a hardpoint, and that is exactly what wants a line.
             */
            log = true,
        ): void => {
            // Nothing to do, and that includes the EXPLOSION below. The guard used to live inside the
            // updater alone, so the set was left correct while everything after it ran again: firing at
            // a hardpoint that was already gone replayed its death blast and dropped a second wreck.
            // Reported. `destroyAll` leaned on it too, over every hardpoint at once.
            if (destroyedRef.current.has(id) === isDestroyed) {
                return;
            }

            setDestroyed((current) => {
                if (current.has(id) === isDestroyed) {
                    return current;
                }

                const next = new Set(current);
                if (isDestroyed) {
                    next.add(id);
                } else {
                    next.delete(id);
                }
                return next;
            });

            const hardpoint = scene?.hardpoints.find((h) => h.id === id);

            if (!isDestroyed) {
                // Repairing has to put back everything destroying took, or the two buttons are not
                // opposites. It used to remove the id from the destroyed set and stop there - so a
                // hardpoint shot to nothing came back with its bar still reading 0, and its wreckage
                // still tumbling away beside the hull it had just been restored to.
                //
                // ONE hardpoint's worth. The attacker's Repair target refills the SHIP's pools as well,
                // and those are not this hardpoint's to give back.
                const full = hardpoint?.health ?? null;

                setHardpointHealth((current) => {
                    // Repairing is as much a thing that happened to the unit as destroying is, and the
                    // log reads as a record of both or of neither. The value BEFORE is only knowable
                    // here, so the line is queued from inside the update rather than read from a
                    // closure that may be a render behind.
                    const before = current[id] ?? 0;

                    if (log) {
                        queueMicrotask(() =>
                            setDamageLog((lines) =>
                                appendShot(lines, [
                                    {
                                        kind: "repair",
                                        source: BY_HAND,
                                        amount: Math.max(
                                            (full ?? 0) - before,
                                            0,
                                        ),
                                        target: id,
                                        armor: null,
                                        pool: "hull",
                                        destroyed: false,
                                        workings: [
                                            `Health ${before} -> ${full ?? 0}`,
                                        ],
                                    },
                                ]),
                            ),
                        );
                    }

                    return { ...current, [id]: full };
                });

                viewportRef.current?.clearBreakoff(id);
                breakoffsRef.current = breakoffsRef.current.filter(
                    (entry) => entry.id !== id,
                );

                return;
            }

            // The wreckage the hardpoint sheds. 167 of foc's 355 hardpoints name one; the server has
            // resolved them since H1 and nothing read them, so a destroyed hardpoint used to just vanish.
            const prop =
                hardpoint === undefined
                    ? null
                    : breakoffFor(hardpoint, scene?.breakoffProps ?? []);

            if (
                hardpoint !== undefined &&
                prop !== null &&
                !(viewportRef.current?.hasBreakoff(id) ?? false)
            ) {
                // Fetched on demand: a capital ship names a dozen distinct props and almost none of
                // them is ever dropped in a given session.
                // Resolved HERE, where the hardpoint is in hand, and carried whole: which node a wreck
                // is dropped on is a decision about the hardpoint, not about the scene graph.
                breakoffsRef.current.push({
                    id,
                    prop,
                    at: breakoffAnchor("hull", hardpoint),
                });

                // The key rides on `partId`, which the host echoes back verbatim. The reply carries no
                // model name, and a breakoff must not be mistaken for a scene part - see the handler.
                post({
                    type: "requestGlb",
                    modelReference: prop.modelRef ?? "",
                    partId: `${BREAKOFF_PART}${id}`,
                    // ITS OWN clips, not the subject's. A prop's model shares nothing with the hull's
                    // name, so the scene's list would never have held one of them.
                    animations: prop.animations ?? [],
                });
            }

            playEffect(
                `explosion:${id}:${Date.now()}`,
                hardpoint?.deathExplosionParticles ?? "",
                "hull",
                hardpoint?.attachBone ?? undefined,
                true,
            );

            // Destroying one by hand went unlogged, which made the log a record of SHOTS rather than of
            // what happened to the unit. There is no damage type and no armour factor here - the switch
            // is not a weapon, it just decides the hardpoint is gone - so it says so.
            if (log) {
                setDamageLog((current) =>
                    appendShot(current, [
                        {
                            source: BY_HAND,
                            amount: Number.POSITIVE_INFINITY,
                            target: id,
                            armor: null,
                            pool: "hull",
                            destroyed: true,
                        },
                    ]),
                );
            }
        },
        [scene, playEffect],
    );

    /**
     * Destroys or repairs every hardpoint the XML allows to be destroyed.
     *
     * @param log Whether each hardpoint writes its own line. False from Repair target, which
     *     refills the pools FIRST and writes one line for the whole unit - leaving this on there
     *     produced a line per hardpoint reading "Health 325 -> 325", because the health it was
     *     reporting on had already been put back.
     */
    const destroyAll = useCallback(
        (isDestroyed: boolean, log = true): void => {
            for (const hardpoint of scene?.hardpoints ?? []) {
                if (hardpoint.isDestroyable) {
                    setHardpointDestroyed(hardpoint.id, isDestroyed, log);
                }
            }
        },
        [scene, setHardpointDestroyed],
    );

    useEffect(() => {
        const viewport = viewportRef.current;
        if (viewport === null || scene === null) {
            return;
        }

        for (const hardpoint of scene.hardpoints) {
            if (hardpoint.partId !== null && hardpoint.partId !== undefined) {
                viewport.setPartHidden(
                    hardpoint.partId,
                    partHidden(hardpoint.partId, scene.hardpoints, destroyed),
                );
            }
        }

        // Every decal the scene knows about, and the subset that should be showing.
        viewport.setDecals(
            decalNames(
                scene.hardpoints,
                new Set(scene.hardpoints.map((h) => h.id)),
            ),
            decalNames(scene.hardpoints, destroyed),
        );

        // Never shown in either damage state, so this takes no `destroyed` and does not change with
        // it - it is set here because this is the pass that owns hardpoint-driven visibility.
        viewport.setCollisionMeshes(collisionMeshNames(scene.hardpoints));

        // The ordinary effect rules AND the ability decider. Through the same call as every other
        // effect deliberately: a held proxy then rides the emission gate, so its burst does not
        // burn away unseen and come back exhausted the moment the ability is switched on.
        // The shield mesh is the one piece of GEOMETRY an ability reveals - DEFEND shows it, and
        // the mesh ships hidden, so nothing else would ever bring it up.
        viewport.setShieldRevealed(shieldRevealed(activeAbilities));

        // The cloak's other half. The shell swaps in here and the effects go dark below, because
        // a cloaked unit shows the shell and nothing else at all.
        viewport.setStealthed(stealthed(activeAbilities));

        for (const particle of scene.particles ?? []) {
            viewport.setParticleSystemVisible(
                particle.id,
                effectState(particle.startsVisible, effectDrawsNow(particle)),
            );
        }

        // Both the tree and the group switches read out of the systems that just changed, so this
        // is all it takes to bring the dock along. Destroying a hardpoint lights its smoke and every row
        // that names it says so.
        setTreeItems(viewport.treeItems() ?? []);
    }, [destroyed, scene, abilityProxyIds, activeAbilities, effectDrawsNow]);

    /**
     * Everything the target has left, rebuilt whenever the scene changes.
     *
     * TIER 2 - it resets on open, because the opening rules beat persistence and a fresh preview
     * opens undamaged. Rebuilt from the scene rather than carried, so a subject that declares no
     * pools at all reads as zero rather than as the last ship's numbers.
     */
    useEffect(() => {
        setPools(fullPools(scene));

        setHardpointHealth(
            Object.fromEntries(
                (scene?.hardpoints ?? []).map((h) => [h.id, h.health ?? null]),
            ),
        );

        setFireTarget("hull");
        setAttackerProjectile(null);
        setDamageLog([]);
        requestedClonesRef.current.clear();
    }, [scene]);

    /** Whether the subject has been finished off, and by which clone. */
    const [unitDead, setUnitDead] = useState(false);

    /**
     * Puts the scene back the way it was before the subject died.
     *
     * The inverse of the death watch, in ONE place: the ship's parts come back, the wreck and
     * everything attached to it goes, and the clone is forgotten so it is fetched again. Forgetting
     * it is what lets the death play a second time - the request is guarded against asking twice,
     * and that guard outlived the wreck it was about.
     *
     * The re-fetch is not a cost at the moment of death: the prefetch runs on `unitDead` going
     * false, so the wreck is back on the shelf long before anything can kill the ship again.
     */
    const restoreFromDeath = useCallback((): void => {
        const viewport = viewportRef.current;
        if (viewport === null) {
            return;
        }

        // The wreck comes home BEFORE the parts are shown again. A unit that spun away is sitting
        // wherever it finished, and un-hiding it there would put the ship back some distance off
        // the grid, still rolled - which is what every route back would have looked like.
        viewport.clearSpinAway();

        for (const part of sceneRef.current?.parts ?? []) {
            viewport.setPartHidden(part.id, false);
        }

        for (const clone of sceneRef.current?.deathClones ?? []) {
            dropPassiveEffects(`${DEATH_CLONE_PART}${clone.objectId}`);
            viewport.removePart(`${DEATH_CLONE_PART}${clone.objectId}`);
        }

        requestedClonesRef.current.clear();
    }, [dropPassiveEffects]);

    /** Read by the glb handler, which has to know whether a clone arriving is already needed. */
    const deadRef = useRef(false);

    useEffect(() => {
        deadRef.current = unitDead;
    }, [unitDead]);

    /** Clone models already asked for, so a damage-type change does not re-fetch one. */
    const requestedClonesRef = useRef(new Set<string>());

    /**
     * Fetches the wreck this weapon would leave, BEFORE it is needed.
     *
     * The death clone is the one piece of geometry whose arrival is watched - the ship vanishes and
     * the reader waits for the wreck. A capital ship's clone is three megabytes, so asking for it at
     * the moment of death shows as a gap. Asked for as soon as the damage type says which one it
     * would be, and held hidden by the glb handler until the death watch wants it.
     *
     * Re-runs on a damage-type change, because that changes the answer; each model is asked once.
     */
    useEffect(() => {
        // `unitDead` is a dependency so this runs again when the ship comes BACK: the restore has
        // just forgotten the clone, and this is what puts it on the shelf ready for the next death.
        const clone = cloneForDamage(
            scene?.deathClones ?? [],
            attacker.damageType,
        );
        const model = clone?.modelFile ?? "";

        if (clone === null || model === "") {
            return;
        }

        const partId = `${DEATH_CLONE_PART}${clone.objectId}`;

        if (requestedClonesRef.current.has(partId)) {
            return;
        }

        requestedClonesRef.current.add(partId);
        post({
            type: "requestGlb",
            modelReference: model,
            partId,
            // ITS OWN clips. That a clone ever played at all was an accident of naming: its model
            // is conventionally the hull's name plus a suffix, so its `_die_00.ala` matched the
            // hull's stem and rode along in the subject's list.
            animations: clone.animations ?? [],
        });
    }, [scene, attacker.damageType, unitDead]);

    /**
     * The pools, but only while they still describe the subject on stage.
     *
     * `setPools` runs from an effect and `scene` changes during a render, so there is always one
     * commit where the two disagree - and at MOUNT the effect leaves every pool at 0, which read
     * against the first real subject's declared health is a unit dead on arrival. That is what
     * "loading a unit without hardpoints immediately plays the death explosion" was. Everything
     * that READS a pool goes through here; the writers below run from a click, by which time the
     * two have long since agreed.
     */
    const livePools = useMemo(() => poolsFor(scene, pools), [scene, pools]);

    /**
     * The two pools, corrected against each other exactly as a service tick would.
     *
     * They are SEPARATE - the hull is the unit's own Tactical_Health, the hardpoints are their own
     * sum - and neither may run more than Hull_Vs_Hard_Points_Health_Constraint ahead of the other.
     * The panel used to draw the hardpoint sum as the hull, so the pool that actually kills the unit
     * was never on screen and the leash was not modelled at all.
     *
     * Declared ABOVE the death watch because the watch reads it - a unit with no hardpoints dies
     * when the hull empties, and there is nothing else that could tell it so.
     */
    const healthNow = useMemo(() => {
        // `livePools.hull` is the LIVE number the attacker panel depletes. Without it the bar drew
        // Tactical_Health forever and a reader could shoot a unit all day against a full bar.
        const rawHardpoints = hardpointPool(
            scene?.hardpoints ?? [],
            hardpointHealth,
            destroyed,
        );

        // The all-destroyed branch has its say FIRST: losing the last destroyable hardpoint sets the
        // hull to zero by flat assignment, and everything downstream - the bar, the damage stage,
        // the death watch - reads the hull.
        const rawHull = hullAfterHardpointDeath(
            hullPool(scene?.defence, livePools?.hull),
            scene?.hardpoints ?? [],
            destroyed,
            scene?.defence?.diesWithHardpoints ?? true,
        );

        return leashed(
            rawHull,
            rawHardpoints,
            scene?.defence?.hullVsHardpointsConstraint ??
                DEFAULT_HULL_CONSTRAINT,
            scene?.defence?.diesWithHardpoints ?? true,
        );
    }, [scene, hardpointHealth, destroyed, livePools?.hull]);

    const hull = healthNow.hull;
    const hardpointHealthPool = healthNow.hardpoints;

    /**
     * Whether the HULL is choosing the damage stage rather than the reader.
     *
     * Only when there is a table to choose with: an object that declares none keeps the manual
     * slider in Gameplay too, which is better than a dead control that explains nothing.
     */
    const altDrivenByHull =
        mode === "gameplay" && (scene?.damageTable ?? []).length > 0;

    /**
     * Drives the damage STAGE from the hull, which is what the damage table is for.
     *
     * `Land_Damage_Thresholds` was read by nothing at all before this: the table was parsed for
     * which stages EXIST and its thresholds thrown away, and the ALT slider was hidden in Gameplay
     * on the stated grounds that "the damage state follows from which hardpoints have been
     * destroyed". Nothing made it follow. The stages were simply unreachable there.
     *
     * Gameplay only. In Model mode the slider IS the control, and a hull that has not been shot at
     * would drag it back to zero the moment the reader moved it.
     */
    useEffect(() => {
        if (mode !== "gameplay") {
            return;
        }

        const table = scene?.damageTable ?? [];
        if (table.length === 0) {
            return;
        }

        setAlt(stageForHull(table, hull.max > 0 ? hull.current / hull.max : 1));
    }, [mode, scene, hull.current, hull.max]);

    /**
     * Kills the unit when its hardpoints are gone, or when its own hull pool empties.
     *
     * The rule as the user gave it: a unit with hardpoints cannot be targeted itself, and it dies
     * when ALL of them are dead - untargetable ones included. The game warns about untargetable
     * but destructible, and at least two mods use the combination deliberately, so a hardpoint the
     * reticles never offered still has to die before the ship does.
     *
     * An EVENT, like the hardpoint death explosion: it belongs to the moment the last hardpoint goes,
     * not to the state of being dead, so it fires from the transition rather than from a render.
     */
    useEffect(() => {
        // The hull pool as well as the hardpoints. A unit with no destructible hardpoints dies by
        // its own health, and until that was passed in it could not die at all - which is most
        // units: 188 carry their weapons as WEAPON behaviour and no hardpoints, against 68 with.
        const dead = unitDestroyed(
            scene?.hardpoints ?? [],
            destroyed,
            hull,
            scene?.defence?.diesWithHardpoints ?? true,
        );

        if (!dead) {
            // Coming BACK from death, and only on the transition. This is the exact inverse of what
            // the branch below does, and it lives here so that EVERY route back runs it - the
            // attacker panel's Repair, the Hardpoints section's `Repair all`, or the reader simply
            // un-ticking one hardpoint. `Repair all` is `destroyAll(false)` and nothing else, so with
            // the restore living in `repairTarget` it put the hardpoints back and left the ship hidden
            // under its own wreck, with the wreck's clip clamped at its last frame - after which no
            // death ever played again.
            if (unitDead) {
                restoreFromDeath();
            }

            setUnitDead(false);
            return;
        }

        if (unitDead) {
            return;
        }

        setUnitDead(true);

        // What the SHIP itself sets off. Its own `Death_Explosions`, which is a third thing from a
        // hardpoint's and a breakoff prop's - it goes off where the ship was, so it attaches to the
        // scene rather than to any part. It has to: every part is about to be hidden, and three
        // prunes a hidden subtree, effects included.
        playEffect(
            `deathblast:${Date.now()}`,
            scene?.deathExplosions ?? "",
            undefined,
            undefined,
            true,
        );

        // Which wreck it leaves depends on what KILLED it, which is the weapon in the attacker
        // panel - so the clone follows the damage type set there.
        const clone = cloneForDamage(
            scene?.deathClones ?? [],
            attacker.damageType,
        );

        // SPINNING AWAY: the automated death clone, for a unit that declares none. Measured over
        // both trees, not one of the 34 objects that declare `Spin_Away_On_Death` also declares a
        // `Death_Clone`, so the two are alternatives rather than things that stack - and a clone,
        // where there is one, wins.
        //
        // The hull STAYS on screen for this, which is the whole point: it flies on along its own
        // forward axis, corkscrewing, and comes apart at the end of `Spin_Away_On_Death_Time`.
        const spin = scene?.spinAway ?? null;
        const hullPart =
            (scene?.parts ?? []).find((part) => part.origin === "Hull") ??
            (scene?.parts ?? [])[0];

        if (clone === null && spin !== null && hullPart !== undefined) {
            viewportRef.current?.startSpinAway(hullPart.id, spin, () => {
                // Its OWN explosion - `Spin_Away_On_Death_Explosion`, a different tag from
                // `Death_Explosions` - fired where the wreck finished rather than where it died.
                playEffect(
                    `spinaway:${Date.now()}`,
                    spin.explosion ?? scene?.deathExplosions ?? "",
                    // Where the wreck FINISHED, not where it died. With no position it goes to the
                    // model root, so a fighter that flew 270 units away blew up back at the origin.
                    undefined,
                    undefined,
                    true,
                    spinAwayEnd(spin),
                );

                for (const part of scene?.parts ?? []) {
                    viewportRef.current?.setPartHidden(part.id, true);
                }
            });

            return;
        }

        // The ship is GONE, and that does not wait on a wreck being authored for this damage type.
        // The engine swaps the death clone in for the hull, so the hull goes either way - a clone
        // that does not match the damage type means nothing replaces it, not that it survives.
        for (const part of scene?.parts ?? []) {
            viewportRef.current?.setPartHidden(part.id, true);
        }

        if (clone?.modelFile === null || clone?.modelFile === undefined) {
            return;
        }

        // Usually already here and waiting: the prefetch below asks for it as soon as the damage
        // type says which one it would be. Showing it is all that is left.
        const partId = `${DEATH_CLONE_PART}${clone.objectId}`;

        viewportRef.current?.setPartHidden(partId, false);
        viewportRef.current?.startDeathClip(partId);
    }, [
        scene,
        destroyed,
        hull,
        unitDead,
        attacker.damageType,
        playEffect,
        restoreFromDeath,
    ]);

    /**
     * Whether the ship itself is a legal target.
     *
     * False as soon as it has a destructible hardpoint: the engine offers the hardpoints and nothing else,
     * so "The ship" would be aiming at something no weapon can reach. Disabled rather than removed
     * - the reader needs to see that the choice exists and why it is not available.
     */
    const shipTargetable = useMemo(
        () => unitTargetable(scene?.hardpoints ?? []),
        [scene],
    );

    /**
     * The target's defence as it stands NOW.
     *
     * A ship whose generators are gone is unshielded for every purpose a hit cares about: the
     * shield does not absorb, it does not stop a shield-only bolt, and a hitpoint bolt has nothing
     * to bypass. Applied here rather than at each of the four places that resolve a hit, so they
     * cannot come to disagree about it.
     */
    const liveDefence = useMemo(
        () =>
            scene?.defence === null || scene?.defence === undefined
                ? scene?.defence
                : shieldsDown
                  ? { ...scene.defence, isShielded: false }
                  : scene.defence,
        [scene, shieldsDown],
    );

    /**
     * Fires the configured weapon at whatever is selected.
     *
     * The arithmetic lives in `attacker.ts`; this only routes the answer. A hardpoint reaching zero is
     * handed to the destruction path that already exists, so a shot that kills a hardpoint hides its
     * model, shows its decal, plays its explosion once and drops its breakoff prop - none of which
     * is new here.
     */
    const fire = useCallback(() => {
        const defence = liveDefence;
        const current = livePools;

        if (defence === null || defence === undefined || current === null) {
            return;
        }

        // Off first, so a second press inside the flash restarts it rather than
        // extending the first - the timer below is keyed on the flag going true.
        setJustFired(false);
        requestAnimationFrame(() => setJustFired(true));

        // What the shot is CALLED in the log: the projectile where one was picked, otherwise the
        // damage type, which is the only name a hand-built weapon has.
        const source =
            (scene?.projectiles ?? []).find((p) => p.id === attackerProjectile)
                ?.id ?? attacker.damageType;

        if (fireTarget === "hull") {
            const { pools: after, workings } = resolveHitWithWorkings(
                attacker,
                defence,
                current,
            );

            setPools(after);

            // MEASURED, not recomputed. The difference the shot made to each pool is what actually
            // happened; working the numbers out a second time here would be a second copy of the
            // damage rules, free to disagree with the one that did the work.
            setDamageLog((log) =>
                appendShot(
                    log,
                    [
                        {
                            pool: "shield" as const,
                            took: current.shield - after.shield,
                        },
                        {
                            pool: "energy" as const,
                            took: current.energy - after.energy,
                        },
                        {
                            pool: "hull" as const,
                            took: current.hull - after.hull,
                        },
                    ]
                        .filter((hit) => hit.took > 0 || hit.pool === "hull")
                        .map((hit) => ({
                            source,
                            amount: hit.took,
                            target: scene?.subject ?? "the unit",
                            armor:
                                hit.pool === "shield"
                                    ? (defence.shieldArmorType ?? null)
                                    : hit.pool === "energy"
                                      ? null
                                      : (defence.armorType ?? null),
                            pool: hit.pool,
                            destroyed:
                                hit.pool === "hull" &&
                                after.hull <= 0 &&
                                current.hull > 0,
                            // The whole calculation, on the line it belongs to. Only the pool that took the
                            // hull half carries it - repeating it under the shield line would say the same
                            // arithmetic twice for one shot.
                            workings:
                                hit.pool === "hull" ? workings : undefined,
                        })),
                ),
            );

            return;
        }

        // Who this shot reaches. ONE path, whether or not a projectile was picked: the picker fills
        // the attacker's blast fields and the FIELDS are what fires. It used to branch, and the
        // no-projectile branch hand-built a single hit with no blast - so a reader who typed a
        // blast radius by hand was simulating a bolt that did not go off.
        const positions = Object.fromEntries(
            (scene?.hardpoints ?? []).map((hardpoint) => [
                hardpoint.id,
                viewportRef.current?.bonePosition(
                    hardpoint.partId ?? undefined,
                    hardpoint.attachBone ?? undefined,
                ) ?? null,
            ]),
        );

        const hits = blastVictims(
            attackerProjectileSpec(attacker),
            fireTarget,
            candidatesFrom(positions, fireTarget),
            destroyed,
        );

        const result = fireBlast(
            attacker,
            defence,
            current,
            hits,
            hardpointHealth,
        );

        setPools(result.pools);
        setHardpointHealth(result.hardpointHealth);

        // A hardpoint declares no armour of its own - measured, 0 of them do - so it is hull
        // geometry and takes the HULL's column. Saying so on every line is what makes the log
        // answer "why did that number come out like that".
        setDamageLog((log) =>
            appendShot(log, [
                // The shield first where it took anything: it is a different event from the hull hit
                // that followed, and one line saying both would hide the bypass rule entirely.
                ...(current.shield - result.pools.shield > 0
                    ? [
                          {
                              source,
                              amount: current.shield - result.pools.shield,
                              target: scene?.subject ?? "the unit",
                              armor: defence.shieldArmorType ?? null,
                              pool: "shield" as const,
                              destroyed: false,
                              workings: result.workings,
                          },
                      ]
                    : []),
                // Then each victim, nearest out - the order the blast reached them, which is the order
                // the damage was applied in.
                ...hits.map((hit, index) => ({
                    source,
                    amount:
                        (hardpointHealth[hit.id] ?? 0) -
                        (result.hardpointHealth[hit.id] ?? 0),
                    target: hit.id,
                    armor: defence.armorType ?? null,
                    pool: "hull" as const,
                    destroyed: result.destroyed.has(hit.id),
                    // The split is said once, on whichever line comes first, and each victim then
                    // carries only its own arithmetic.
                    workings:
                        index === 0 && current.shield - result.pools.shield <= 0
                            ? [
                                  ...result.workings,
                                  ...(result.perVictim[hit.id] ?? []),
                              ]
                            : result.perVictim[hit.id],
                })),
            ]),
        );

        for (const id of result.destroyed) {
            // Already logged above, with the real damage type and the number that actually landed.
            setHardpointDestroyed(id, true, false);
        }

        // The target is GONE, so stop aiming at it. Firing again at a hardpoint that is already off
        // logged a "did no damage" line and replayed its death blast - reported. The blast is fixed
        // at its root in `setHardpointDestroyed`; this is the other half, and the useful one: the
        // engine offers no such target either, so the panel should not keep it under the crosshair.
        if (result.destroyed.has(fireTarget)) {
            setFireTarget("hull");
        }
    }, [
        attacker,
        attackerProjectile,
        scene,
        liveDefence,
        livePools,
        hardpointHealth,
        fireTarget,
        destroyed,
        setHardpointDestroyed,
    ]);

    /** Puts the target back together without touching the weapon you built. */
    const repairTarget = useCallback(() => {
        // The hardpoints are back, so the wreckage they shed goes with them.
        viewportRef.current?.clearBreakoffs();
        breakoffsRef.current = [];

        // The wreck and the hidden ship are NOT this function's to put back. The death watch owns
        // that, because `Repair all` never comes through here at all - see `restoreFromDeath`.

        const full = fullPools(scene);

        setPools((current) => {
            // One line for the whole unit rather than one per hardpoint: the reader pressed one
            // button, and ten lines would bury the shots that came before it.
            const before = current?.hull ?? 0;

            queueMicrotask(() =>
                setDamageLog((lines) =>
                    appendShot(lines, [
                        {
                            kind: "repair",
                            source: BY_HAND,
                            amount: Math.max(full.hull - before, 0),
                            target: scene?.subject ?? "the unit",
                            armor: null,
                            pool: "hull",
                            destroyed: false,
                            workings: [
                                l10n.t("Hull {0} -> {1}", before, full.hull),
                                l10n.t(
                                    "Hardpoints restored to their declared Health",
                                ),
                            ],
                        },
                    ]),
                ),
            );

            return full;
        });

        setHardpointHealth(
            Object.fromEntries(
                (scene?.hardpoints ?? []).map((h) => [h.id, h.health ?? null]),
            ),
        );
    }, [scene]);

    /**
     * Whether the clip on the playhead shows the unit DEPLOYED (P5).
     *
     * The preview keeps no deployed state of its own - the clip is the model being shown in that state.
     * A `deployed_*` clip counts throughout, the deploy clip once it is held at its end, and nothing else.
     * Recomputed as the playhead moves, but it is a boolean, so what hangs off it changes only at the
     * moment the state does.
     */
    const deployed = useMemo(
        () =>
            animation !== null &&
            isDeployedClip(
                readClip(clipNamingModel(scene), animation),
                playhead.duration > 0 &&
                    playhead.time >= playhead.duration - 1e-4,
            ),
        [scene, animation, playhead.time, playhead.duration],
    );

    /**
     * The scene's weapons as the engine reads them in that state: a deploying walker's arc and turret
     * limits swap to the deployed pair, which is unrestricted unless written. Everything below - the
     * rows, the cones, the handles, the cards - reads this rather than `scene.weapons`, so none of them
     * needs to know deployment exists.
     */
    const stateWeapons = useMemo(
        () => weaponsInState(scene?.weapons ?? [], deployed),
        [scene?.weapons, deployed],
    );

    /**
     * The weapon weapons, as both the dock and the viewport see them.
     *
     * One derivation feeding both. Building the dock's rows and the viewport's cones separately is
     * what let a hardpoint's arc keep hanging in the air after the hardpoint it belongs to had been
     * blown off - two readings of the same damage state, and only one of them updated.
     */
    const weapons = useMemo(
        () =>
            weaponRows(
                { weapons: stateWeapons, hardpoints: scene?.hardpoints ?? [] },
                destroyed,
            ),
        [stateWeapons, scene?.hardpoints, destroyed],
    );

    /**
     * Every turret that can actually be swung, from BOTH places one can be declared.
     *
     * The AT-AA's is on its unit WEAPON and it has no hardpoints at all, so reading hardpoints alone
     * found nothing to sweep on the very unit this exists for.
     */
    const sweepable = useMemo(
        () =>
            turretSweeps(
                (scene?.hardpoints ?? []).map((h) => ({
                    id: h.id,
                    partId: h.partId ?? "hull",
                    turret: h.turret,
                })),
                weapons.map((w) => ({
                    id: w.id,
                    partId: w.partId,
                    turret: w.turret,
                })),
            ),
        [scene, weapons],
    );

    /**
     * The turrets the reader can drag: every one that declares a traverse, in every lens. Pointing at
     * a turret draws its handles; see `TurretRig.setTurretHandles`.
     */
    // From the WEAPONS, in the arc toggle's own ids - not from `sweepable`, whose hardpoint turrets
    // are keyed by the bare hardpoint id and so never matched. See `handlesForShownArcs`.
    const draggableTurrets = useMemo(
        () => everyTurretHandle(weapons),
        [weapons],
    );

    /**
     * The turrets whose handles stay drawn whatever the pointer does: exactly those whose arcs are
     * shown. The arc state is persistent by design, at three levels - the stage pill, a toggle per
     * weapon row, a fire bone in the tree - so a reader working down the rows in the dock keeps the
     * manipulators on screen while the pointer is away from them.
     */
    const shownTurrets = useMemo(
        () =>
            new Set(
                visibleArcs(weapons, fireArcs, hiddenWeapons).map(
                    (arc) => arc.weaponId,
                ),
            ),
        [weapons, fireArcs, hiddenWeapons],
    );

    /* One card per hardpoint, each carrying the weapon on it, and whatever weapons are left over. */
    const cards = useMemo(
        () =>
            hardpointCards(
                { weapons: stateWeapons, hardpoints: scene?.hardpoints ?? [] },
                destroyed,
            ),
        [stateWeapons, scene?.hardpoints, destroyed],
    );

    const looseWeapons = useMemo(() => unitWeapons(weapons), [weapons]);

    /**
     * Every weapon starts OFF, and the stage pill switches the lot together.
     *
     * The user's rule. The measured reason: the Nebulon B's four hardpoints each declare 175 by 160
     * degrees, so drawing them at once fills the viewport however right the geometry is.
     *
     * Keyed on the SUBJECT, not on the weapon rows: those are rebuilt whenever `destroyed` changes
     * too, so keying on them meant shooting a hardpoint silently switched every arc off.
     */
    useEffect(() => {
        setHiddenWeapons(allWeaponIds(weapons));
    }, [scene]);

    return {
        damageLog,
        setDamageLog,
        logHeight,
        logResizeN,
        logWidth,
        logResizeW,
        logOpen,
        setLogOpen,
        justFired,
        logRef,
        destroyed,
        setDestroyed,
        sweeping,
        setSweeping,
        attacker,
        setAttacker,
        attackerPresets,
        setAttackerPresets,
        presetName,
        setPresetName,
        projectileSearch,
        setProjectileSearch,
        projectileNote,
        setProjectileNote,
        setAttackerProjectile,
        setEnergyPool,
        poolOptions,
        hardpointHealth,
        fireTarget,
        setFireTarget,
        hiddenWeapons,
        setHiddenWeapons,
        turretAims,
        setTurretAims,
        setHardpointDestroyed,
        destroyAll,
        deadRef,
        livePools,
        hull,
        hardpointHealthPool,
        altDrivenByHull,
        shipTargetable,
        liveDefence,
        fire,
        repairTarget,
        weapons,
        sweepable,
        draggableTurrets,
        shownTurrets,
        cards,
        looseWeapons,
    };
}
