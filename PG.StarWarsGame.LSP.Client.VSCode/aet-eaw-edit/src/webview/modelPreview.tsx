// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The 3D preview panel: a canvas with the tool dock on the right, matching the layout the story
// graph, the localisation editors and the encyclopedia card already use.
//
// The dock's three levels have fixed MEANINGS, and a control goes in the one matching what it IS:
// the header reports state about the document (the subject and the severity tag), the content is
// this editor's library (the model's own stats and hardpoints), and the foot holds EVERY view
// control - camera angle, grid, which animation. Camera buttons floating over the canvas were the
// obvious place to write them and the wrong place to put them.
//
// The problems bar pops up from the bottom of the STAGE COLUMN, beside a full-height dock - never
// underneath it, and never as a list inside the dock, which is narrow enough to truncate every
// message. The localisation editor is the reference for this whole family of layout questions.
//
// React owns the chrome and nothing else. The renderer is a plain imperative object in
// preview/viewport.ts, held in a ref, because the two lifecycles do not line up: React re-renders
// when a checkbox moves, and a render loop must not be torn down for that.

import "./shared/l10nSetup";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { createRoot } from "react-dom/client";
// Static, not dynamic: the webview is bundled as an IIFE, which cannot be code-split, so an
// `await import()` here would not build.

import type { PreviewBreakoffProp } from "../protocol/modelPreview";
import {
    ModelDetail,
    PreviewProblem,
    PreviewScene,
} from "../protocol/modelPreview";
import { collectTextureNames } from "./preview/materials";
import { loadState, type LoadTally } from "./preview/loadProgress";
import { handoffFor } from "./preview/handoff";
import { groundRange } from "./preview/groundRange";
import { StageSlots } from "./shared/Stage";
import { AbilitiesFlyout } from "./preview/panels/AbilitiesFlyout";
import { AbilityCommandBar } from "./preview/panels/AbilityCommandBar";
import { AbilityInfoFlyout } from "./preview/panels/AbilityInfoFlyout";
import { AttackControls } from "./preview/panels/AttackControls";
import { SubjectStateControls } from "./preview/panels/SubjectStateControls";
import { CardInfoFlyout } from "./preview/panels/CardInfoFlyout";
import { DamageLogFlyout } from "./preview/panels/DamageLogFlyout";
import { StageTopEdge } from "./preview/panels/StageTopEdge";
import { DockContent } from "./preview/panels/DockContent";
import { CaptureFrame } from "./preview/panels/CaptureFrame";
import { LoadCover } from "./preview/panels/LoadCover";
import { SceneFlyout } from "./preview/panels/SceneFlyout";
import { CameraFlyout } from "./preview/panels/CameraFlyout";
import { WeaponFlyout } from "./preview/panels/WeaponFlyout";
import { PreviewProblems } from "./preview/panels/PreviewProblems";
import { DockHeader } from "./preview/panels/DockHeader";
import { DockFoot } from "./preview/panels/DockFoot";
import { NOTHING_DRAWN, PRESETS } from "./preview/previewLabels";
import { post } from "./preview/previewPost";
import { useCaptureSettings } from "./preview/useCaptureSettings";
import { useInspector } from "./preview/useInspector";
import { useHardpointList } from "./preview/useHardpointList";
import { useSubjectSync } from "./preview/useSubjectSync";
import { useStoredState } from "./preview/useStoredState";
import { useNotices } from "./preview/useNotices";
import { useOverlayPills } from "./preview/useOverlayPills";
import { useCameraBindings } from "./preview/useCameraBindings";
import { useFlyouts } from "./preview/useFlyouts";
import { useHostMessages } from "./preview/useHostMessages";
import { useParticleEffects } from "./preview/useParticleEffects";
import { useModelTree } from "./preview/useModelTree";
import { useAbilities } from "./preview/useAbilities";
import { useAnimationTransport } from "./preview/useAnimationTransport";
import { useCombatLens } from "./preview/useCombatLens";
import { WRECK_EFFECT } from "./preview/partIds";
import { ancestorsOf } from "./preview/skeleton";
import {
    boneIndexOfRow,
    toggleSelected,
    type TreeItem,
} from "./preview/previewTree";
import {
    BREAKOFF_ATTACHMENT,
    PreviewViewport,
    type ViewportStats,
} from "./preview/viewport";
import { Shell } from "./preview/previewShell";
import { parseHex } from "./preview/colour";
import { reviewFactionColour } from "./preview/colourUsability";
import { PreviewAssetLoader } from "./preview/previewAssetLoader";
import { useRoomSettings } from "./preview/useRoomSettings";
import { useViewportSync } from "./preview/useViewportSync";
import {
    type DefinedLevels,
    type LevelCost,
    levelSteps,
    withDeclaredStages,
} from "./preview/levels";
import { type ParticleGroup, particleGroups } from "./preview/particleScene";
import { type PreviewMode } from "./preview/previewMode";
import { boneRowIndex } from "./preview/weaponRows";
import { type ReticleState } from "./preview/reticles";
import { type BreakoffAnchor } from "./preview/breakoff";
import { type SubjectState } from "../shared/subjectState";
import {
    type DirectionalName,
    type DirectionalSetting,
} from "../shared/viewerSettings";
import { shadowTintReach } from "./preview/shadowVolumePass";
import { RightDock } from "./shared/RightDock";

import { initPanelLayout } from "./shared/panelLayoutBridge";
import { vscodeApi } from "./shared/vscodeApi";

// Reads the dock and drawer sizes the host seeded into the page, and reports
// every drag back to it. Must run before anything measures itself.
initPanelLayout(vscodeApi);

/**
 * How long the load may go silent before the cover comes off anyway, in milliseconds.
 *
 * Idle, not total - see the effect that uses it.
 */
const LOAD_IDLE_LIMIT = 8000;

function ModelPreview(): React.JSX.Element {
    const canvasRef = useRef<HTMLCanvasElement | null>(null);
    const labelsRef = useRef<HTMLDivElement | null>(null);
    const viewportRef = useRef<PreviewViewport | null>(null);

    const [scene, setScene] = useState<PreviewScene | null>(null);
    const [stats, setStats] = useState<ViewportStats | null>(null);
    const [problems, setProblems] = useState<PreviewProblem[]>([]);
    const [problemsOpen, setProblemsOpen] = useState(false);
    /** Nothing is written back until the stored room has been applied, or we would save defaults. */
    const restoredRef = useRef(false);

    // The room: every Tier 1 value, its restore, its reset and its save. See `useRoomSettings`.
    const room = useRoomSettings({
        restored: () => restoredRef.current,
        save: (settings) => post({ type: "setViewerSettings", settings }),
        isPreset: (view) => PRESETS.some((p) => p.view === view),
    });
    const {
        grid,
        setGrid,
        floor,
        setFloor,
        floorLevel,
        wireframe,
        setWireframe,
        heatOn,
        heatDebug,
        bloom,
        lights,
        setLights,
        cameraPresets,
        cameraBindings,
        wind,
        weatherIntensity,
        background,
        cameraView,
        setCameraView,
        drawDistance,
        skeletonOn,
        setSkeletonOn,
        labelMode,
        setLabelMode,
        folded,
        setFolded,
        translatedOn,
        particlesOn,
        setParticlesOn,
        particleSpeed,
        fireArcs,
        setFireArcs,
        applyRoom,
    } = room;

    /** The preset whose name is being edited in its row, if any. */
    const [renamingPreset, setRenamingPreset] = useState<string | null>(null);

    const captureSettings = useCaptureSettings();
    const {
        captureSizeId,
        setCaptureSizeId,
        customCapture,
        setCustomCapture,
        captureDims,
        captureTransparent,
        setCaptureTransparent,
        captureScenery,
        setCaptureScenery,
    } = captureSettings;
    /** Whether the scene holds a weather field - the one thing the intensity reaches. */
    const [hasWeather, setHasWeather] = useState(false);

    /**
     * Which lens the dock is showing.
     *
     * A SOFT switch: it changes which tools are on screen and nothing else, so an animation keeps
     * playing while the reader hops into Model mode to hide a mesh.
     */
    const [mode, setMode] = useState<PreviewMode>("model");

    /** Which light the Light section's four dials are editing. The rig has three and they share one row. */
    const [editing, setEditing] = useState<DirectionalName>("sun");

    const cameraState = useCameraBindings({
        cameraBindings,
        cameraPresets,
        editing,
        lights,
        scene,
        setCameraView,
    });
    const { authorCameras, cameraViews, applyBoundCamera } = cameraState;

    /**
     * The reader's own word about how to tint the hull, recorded as well as applied.
     *
     * The refs hold what they CHOSE; the state holds what this subject could honour. They have to be
     * two things: opening a unit whose roster lacks the chosen faction falls back to no tint, and
     * writing that fallback into the ref would throw the choice away on the way past. Which is the
     * half that was missing - the ref was only ever set from stored settings, so a faction picked
     * mid-session was lost at the next subject and the STARTUP value came back instead.
     */
    const chooseTint = (name: string, colour: string | null): void => {
        storedFactionRef.current = name === "" ? null : name;
        storedCustomColourRef.current = colour;
        setFaction(name);
        setCustomColour(colour);
    };

    /** Changes one directional, leaving the other two and the global terms alone. */
    const setLight = (
        name: DirectionalName,
        change: Partial<DirectionalSetting>,
    ): void =>
        setLights((current) => ({
            ...current,
            [name]: { ...current[name], ...change },
        }));

    /** The faction the reader last chose, by name, until this subject's own list arrives. */
    const storedFactionRef = useRef<string | null>(null);
    /** The colour they picked by hand instead, which every subject can wear. */
    const storedCustomColourRef = useRef<string | null>(null);

    /**
     * This subject's state from earlier in the session, until the scene arrives to apply it to.
     *
     * Held rather than applied on arrival: it lands before the scene, and the scene handler resets
     * every one of these fields on its way in.
     */
    const storedSubjectRef = useRef<SubjectState | null>(null);

    /**
     * Whether the scene being handled is a re-read rather than an opening.
     *
     * A ref and not state: it is read inside the message handler while parts stream in, and a state
     * update would not be visible to the closure that is already running.
     */
    const refreshingRef = useRef(false);

    const player = useAnimationTransport({
        scene,
        stats,
        viewportRef,
    });
    const {
        animation,
        setAnimation,
        animationPaused,
        animationSpeed,
        animationLoop,
        playhead,
        setPlayhead,
        scrubbingRef,
    } = player;

    /**
     * Drops the selection, and with it the box drawn round it in the viewport.
     *
     * The bone labels it lit go too, because they are derived from it: a label left lit for a row
     * nothing is pointing at is the same defect in a different place.
     */
    const clearSelection = useCallback(() => {
        setSelected(new Set());
        setAnchor(null);
    }, []);

    const toggleSection = useCallback((id: string) => {
        setFolded((current) => {
            const next = new Set(current);

            if (!next.delete(id)) {
                next.add(id);
            }

            return next;
        });
    }, []);

    /**
     * Attachments that currently have nowhere to hang, as of the last part to arrive.
     *
     * Its own slot rather than a push into `problems`, because it is a SNAPSHOT of the scene as it
     * stands - every entry has to be able to go away again when the part it names turns up.
     */
    const [attachmentIssues, setAttachmentIssues] = useState<string[]>([]);

    /**
     * The tree row whose details are open, if any.
     *
     * Deliberately NOT the selection. Details used to appear in the dock the moment anything was
     * picked, which made every click in the tree a page of text nobody asked for - and selecting is
     * how you aim the viewport, not how you ask what something is made of. It takes its own button
     * on the row now, and one row at a time.
     */
    const [detailsRow, setDetailsRow] = useState<string | null>(null);

    /**
     * Whether the ground slider is being DRAGGED rather than stepped with the keyboard.
     *
     * The detent at zero exists because a pointer cannot reliably hit one position in four hundred.
     * A keyboard can: an arrow key moves exactly one step, so a reader who steps to the first mark
     * above the ground meant it and must get it. The flag is what tells the two apart - the change
     * event itself does not say where it came from.
     */
    const groundDragging = useRef(false);

    /**
     * How far into loading this subject the preview is.
     *
     * The viewport is covered while it runs - see `loadProgress` for why the load is worth hiding
     * rather than reordering.
     */
    const [tally, setTally] = useState<LoadTally>({
        expectedParts: null,
        arrivedParts: 0,
        requestedAssets: 0,
        settledAssets: 0,
        timedOut: false,
    });

    /**
     * Whether any `.fx` source was reachable at all.
     *
     * The difference between "nothing translated" and "there was nothing to translate". The game
     * ships COMPILED `.fxo`, not `.fx`, so without a shader directory configured every effect comes
     * back null and the count is 0 for a reason the reader can actually act on.
     */
    const [anyShaderSource, setAnyShaderSource] = useState(false);
    /** Whether this scene asked for any effect at all; a particle system asks for none. */
    const [shaderRequested, setShaderRequested] = useState(false);

    /** How many sub-meshes that actually reaches, out of how many are loaded. */
    const [translatedCount, setTranslatedCount] = useState({
        translated: 0,
        total: 0,
    });

    /**
     * The textures and effects the geometry needs in order to be drawn: asked for once each,
     * counted for the loading cover, and applied to the viewport as the replies land. See
     * `preview/previewAssetLoader.ts`; the panel only hears about what it shows.
     */
    const [assets] = useState(() => {
        const loader: PreviewAssetLoader = new PreviewAssetLoader(
            () => viewportRef.current,
            {
                post: (message) => post(message),
                ledgersChanged: () =>
                    setTally((current) => ({
                        ...current,
                        requestedAssets: loader.requested,
                        settledAssets: loader.settled,
                    })),
                shaderRequested: () => setShaderRequested(true),
                shaderSourceFound: () => setAnyShaderSource(true),
                translated: (coverage) => setTranslatedCount(coverage),
                problem: (severity, message) =>
                    setProblems((current) =>
                        current.some((p) => p.message === message)
                            ? current
                            : [...current, { severity, message }],
                    ),
            },
        );
        return loader;
    });

    /** Everything the model is made of, for the one tree. */
    const [treeItems, setTreeItems] = useState<TreeItem[]>([]);

    /**
     * What the server knows about the hull that the glTF cannot say: bone transforms in the file's
     * own axes, the bounding boxes the file stores, and the proxies - which the exporter writes into
     * the glTF not at all.
     */
    const [modelDetail, setModelDetail] = useState<ModelDetail | null>(null);

    /** The model the outstanding detail request was for, so a crossed reply can be spotted. */
    const detailWantedRef = useRef<string | null>(null);

    /** Multi-select, so a run of meshes can be switched together. */
    const [selected, setSelected] = useState<ReadonlySet<string>>(new Set());
    const [anchor, setAnchor] = useState<string | null>(null);
    /* No `selectedBone` state. It was a single index kept beside `selected`, which is a set of
       rows - two pieces of state for one fact, and the smaller one won: picking a second fire bone
       drew its axes and silently took the first one's away. The indices are DERIVED below. */

    const flyouts = useFlyouts({
        clearSelection,
        detailsRow,
        selected,
    });
    const {
        worldOpen,
        setWorldOpen,
        cameraOpen,
        setCameraOpen,
        abilitiesOpen,
        setAbilitiesOpen,
        attackOpen,
        setAttackOpen,
        abilitiesRef,
        searchPressed,
        infoCard,
        setInfoCard,
        cardInfoRef,
        cardInfoAt,
        infoAbility,
        setInfoAbility,
        abilityInfoRef,
        abilityInfoAt,
        hardpointSearchPressed,
    } = flyouts;

    // Nothing sets this yet: #289 adds the particle transport that pauses them.
    const [particlesPaused] = useState(false);
    /** Emitter names in file order. Names repeat, so the index is the identity, not the name. */
    const [emitters, setEmitters] = useState<string[]>([]);
    const [hiddenEmitters, setHiddenEmitters] = useState<ReadonlySet<number>>(
        new Set(),
    );

    const [reticlesOn, setReticlesOn] = useState(false);

    const abilityState = useAbilities({
        scene,
        setAnimation,
        viewportRef,
    });
    const {
        activeAbilities,
        setActiveAbilities,
        abilityProxyIdsRef,
        activeAbilitiesRef,
        unboundEffectsRef,
        replacedEffectsRef,
        abilityProxyIds,
    } = abilityState;

    /**
     * Which of the seven targeting states the marks are drawn in.
     *
     * Enemy is where it opens: it is what you see looking at somebody else's ship, which is the
     * case a modder is checking. Tier 2 at most - it describes what you are inspecting right now,
     * not a habit - so it resets with the subject rather than being stored.
     */
    // Fixed. The tracked twin comes from HOVERING a mark, which is what the game does, so there is
    // no state to choose - see the note in the Hardpoints section. The other four states
    // (friendly, repairing, disabled and its tracked form) have no way in at present.
    const reticleState: ReticleState = "enemy";

    /** Empty means the model's own colours; otherwise the faction whose tint is applied. */
    const [faction, setFaction] = useState("");
    const [customColour, setCustomColour] = useState<string | null>(null);

    const [alt, setAlt] = useState(0);
    const [lod, setLod] = useState(0);

    /**
     * Whether the user has picked a detail level themselves.
     *
     * Until they do, the preview follows the model: it opens at the HIGHEST level, which is the
     * close-up one. The engine's numbering runs the opposite way to most - LOD0 is the distant,
     * lowest-detail mesh - so defaulting to zero opened every model on its crudest version.
     */
    const lodChosenRef = useRef(false);
    const [levels, setLevels] = useState<DefinedLevels>({ alt: [0], lod: [0] });

    const tree = useModelTree({
        alt,
        hiddenEmitters,
        levels,
        lod,
        searchPressed,
        selected,
        setAlt,
        setHiddenEmitters,
        setLod,
        setTreeItems,
        treeItems,
        viewportRef,
    });
    const { collapsed, setCollapsed, foldedRef, boneFilter, setBoneFilter } =
        tree;

    /* Slider positions over the levels this model DEFINES. Rebuilt only when the level list
       changes, which is once per subject. */
    const altSteps = useMemo(() => levelSteps(levels.alt), [levels.alt]);
    const lodSteps = useMemo(() => levelSteps(levels.lod), [levels.lod]);

    /**
     * Where the shadow colour can be seen, which decides whether its control is live.
     *
     * Depends on `translatedOn` because the stencil pass follows the renderer, and on `stats`
     * because a model with no authored volume casts nothing through it - and whether one is loaded
     * is not known until geometry arrives.
     */
    const shadowReach = useMemo(
        () =>
            shadowTintReach(
                floor,
                viewportRef.current?.castsStencilShadows() ?? false,
            ),
        [floor, translatedOn, stats],
    );

    /**
     * What each detail level costs, for the LOD slider's own label.
     *
     * Depends on `stats` for the reason the arcs and the reticles do - it is this component's
     * signal that geometry has arrived - and on `alt`, because a mesh tagged for another damage
     * stage is not part of what the level draws.
     */
    const lodCost = useMemo(
        () =>
            viewportRef.current?.costByLod(levels.lod) ??
            new Map<number, LevelCost>(),
        [levels.lod, alt, stats],
    );

    /**
     * Wreckage waiting on its model to arrive.
     *
     * Same shape as the death-explosion queue beside it, and for the same reason: the GLB is asked
     * for at the moment of destruction, and the debris is spawned when it lands.
     */
    const breakoffsRef = useRef<
        {
            id: string;
            prop: PreviewBreakoffProp;
            at: BreakoffAnchor;
        }[]
    >([]);

    /**
     * The hardpoints destroyed now, for the effect callbacks and the message handler, which run outside
     * a render.
     *
     * Geometry and effects arrive asynchronously, so a part or system landing after a hardpoint
     * was destroyed has to see the current state rather than the one captured when the handler
     * was made.
     *
     * Made here rather than in the combat lens that owns `destroyed` and writes it, because those
     * callbacks are made before the lens and the lens is handed them.
     */
    const destroyedRef = useRef<ReadonlySet<string>>(new Set());
    /** Same reason as {@link destroyedRef}: the message handler outlives any one scene. */
    const sceneRef = useRef<PreviewScene | null>(scene);
    sceneRef.current = scene;

    /**
     * The bulk switches this scene offers, derived from it rather than stored.
     *
     * Deriving is what keeps the panel and the tree from disagreeing. The old version held a
     * `hiddenGroups` set beside the tree's own state and had to be re-seeded from the damage rules
     * in two places; miss one and the list claimed "off" about something visibly burning.
     */
    const groups = useMemo(
        () =>
            particleGroups({
                particles: scene?.particles ?? [],
                hardpoints: scene?.hardpoints ?? [],
            }),
        [scene],
    );

    const inspection = useInspector({
        detailWantedRef,
        detailsRow,
        modelDetail,
        setDetailsRow,
        treeItems,
        viewportRef,
    });

    /**
     * How far the ground can be moved on this subject.
     *
     * Keyed on `treeItems` for the same reason the inspection is: that is what a load, a level
     * change or a reload rebuilds, so the track is re-scaled with the model rather than staying
     * sized for the last one.
     */
    const ground = useMemo(
        () => groundRange(viewportRef.current?.subjectSphere.radius ?? 0),
        [treeItems],
    );

    // ── the renderer's lifetime, which is the canvas's and not the component's state ──
    useEffect(() => {
        if (canvasRef.current === null || labelsRef.current === null) {
            return;
        }

        const viewport = new PreviewViewport(
            canvasRef.current,
            labelsRef.current,
        );
        viewport.expose();
        viewportRef.current = viewport;

        // The renderer, reachable from the page. Everything that decides whether a mesh is on
        // screen - the level gates, the visibility overrides, the material each sub-mesh ended up
        // with - lives inside this object, and the standing rule is that rendering questions get
        // settled by driving the real thing rather than by reading the code. Without a handle the
        // playwright harness can only photograph the canvas, which is how "the collision hull is
        // white" and "some checkboxes do nothing" both went a long time without a cause.
        (window as unknown as { aetDebugViewport?: unknown }).aetDebugViewport =
            viewport;

        // The end of a piece of wreckage. `Death_Explosions` fires where it FINISHED - the
        // viewport keeps an empty marker there for exactly this - and the fire it was trailing
        // stops with it, which is the one thing an explosion does not do on its own.
        viewport.onBreakoffExpired = (key, prop): void => {
            viewport.removeParticleSystem(`${WRECK_EFFECT}${key}`);
            dropPassiveEffects(`${BREAKOFF_ATTACHMENT}${key}`);

            playEffect(
                `explosion:${key}:${Date.now()}`,
                prop.deathExplosions ?? "",
                `${BREAKOFF_ATTACHMENT}${key}`,
                undefined,
                true,
            );
        };

        // Clicking a joint selects it in the tree; selecting in the tree highlights the joint. One
        // selection, two ways in, so neither view can disagree with the other about what is chosen.
        viewport.onBoneSelected = (index) => {
            if (index !== null) {
                setCollapsed((current) => {
                    const next = new Set(current);
                    // Tree ids, not raw bone indices: the tree holds meshes and emitters too, so a
                    // bone is keyed `bone:<index>` to keep the three kinds from colliding.
                    for (const ancestor of ancestorsOf(
                        viewport.skeleton(),
                        index,
                    )) {
                        next.delete(`bone:${ancestor}`);
                    }
                    return next;
                });
            }
        };

        // The viewport owns the POSE - it is a bone quaternion, and a re-render that cleared it
        // would snap a turret back to rest mid-drag. What comes back here is a copy for the row to
        // read out, never the source.
        viewport.onTurretAimed = (id, aim) =>
            setTurretAims((current) => {
                const next = new Map(current);
                next.set(id, aim);
                return next;
            });

        // Orbiting leaves the preset behind, so the preset stops claiming to be where you are.
        viewport.onCameraMoved = () => setCameraView(null);
        viewport.onReticleClicked = (id) => selectHardpoint(id);

        const observer = new ResizeObserver(() => viewport.resize());
        observer.observe(canvasRef.current);

        return () => {
            observer.disconnect();
            viewport.dispose();
            viewportRef.current = null;
        };
    }, []);

    const loading = loadState(tally);

    /**
     * Gives up on a load that has stopped moving.
     *
     * An IDLE deadline, not a total one: a capital ship on a cold index legitimately takes a while,
     * and a fixed budget would either cut that short or be so generous it never fires. What is
     * actually wrong is nothing arriving - a request the server never answers - and eight seconds
     * of silence says that whether the subject is a trooper or a Star Destroyer.
     *
     * The cover coming off early is the mild failure. Leaving it down over a model that is
     * perfectly visible underneath is the bad one.
     */
    useEffect(() => {
        if (!loading.covered) {
            return;
        }

        const timer = setTimeout(
            () => setTally((current) => ({ ...current, timedOut: true })),
            LOAD_IDLE_LIMIT,
        );

        return () => clearTimeout(timer);
    }, [
        loading.covered,
        tally.arrivedParts,
        tally.settledAssets,
        tally.expectedParts,
    ]);

    const refreshStats = useCallback(() => {
        const viewport = viewportRef.current;
        setStats(viewport?.stats() ?? null);
        setHasWeather(viewport?.hasWeather() ?? false);
        setTreeItems(viewport?.treeItems() ?? []);
        // The geometry's own tags, WIDENED by what the object declares. A damage stage may touch
        // no geometry at all - see `withDeclaredStages` - so the model alone under-reports it.
        const defined = withDeclaredStages(
            viewport?.definedLevels() ?? { alt: [0], lod: [0] },
            sceneRef.current?.damageStages ?? [],
        );
        setLevels(defined);

        // Geometry loaded and not one mesh reaching the screen is worth saying out loud. It is a
        // real authoring state - every sub-mesh tagged for a damage or detail level this model
        // never selects, or the whole hull attached to a bone that ships hidden - and it is
        // indistinguishable from a broken preview if the panel just draws an empty grid.
        const current = viewport?.stats() ?? null;
        if (current !== null && current.parts > 0 && current.meshes === 0) {
            setProblems((existing) =>
                existing.some((p) => p.message === NOTHING_DRAWN)
                    ? existing
                    : [
                          ...existing,
                          { severity: "warning", message: NOTHING_DRAWN },
                      ],
            );
        }

        // Not until geometry has actually ARRIVED. `definedLevels` seeds its sets with 0, so an
        // empty model reports `lod: [0]` - a full-looking answer that is really "nothing has told me
        // anything yet". Choosing from it picked LOD 0 and latched, and since Alamo numbers detail
        // the other way up, every model opened at its LOWEST detail and stayed there. The guard
        // below tested for an EMPTY list, which that placeholder never is.
        if (
            lodChosenRef.current ||
            (current?.parts ?? 0) === 0 ||
            defined.lod.length === 0
        ) {
            return;
        }

        // Where this subject was left, but only if the model still HAS that level. A mesh's `_ALT2`
        // suffix can be edited away between one open and the next, and restoring a level that no
        // longer exists shows nothing at all - indistinguishable from a broken model.
        const previous = storedSubjectRef.current;

        if (previous !== null && defined.alt.includes(previous.alt)) {
            setAlt(previous.alt);
        }

        lodChosenRef.current = true;
        setLod(
            previous !== null && defined.lod.includes(previous.lod)
                ? previous.lod
                : defined.lod[defined.lod.length - 1],
        );
    }, []);

    const particles = useParticleEffects({
        abilityProxyIdsRef,
        activeAbilitiesRef,
        assets,
        destroyedRef,
        refreshStats,
        replacedEffectsRef,
        setHiddenEmitters,
        unboundEffectsRef,
        viewportRef,
    });
    const {
        systemsRef,
        systemScalesRef,
        pendingRef,
        requestedSystemsRef,
        passiveEffectIdsRef,
        explosionsRef,
        effectDrawsNow,
        queuePassiveEffects,
        dropPassiveEffects,
        attachPending,
        playEffect,
    } = particles;

    /**
     * Switches one weapon's arc on or off.
     *
     * Stores the weapons that are OFF, so the set is empty on a fresh subject however many hardpoints it
     * carries, and an id the reader never touched needs no entry at all.
     */
    const setWeaponArcs = useCallback((id: string, on: boolean): void => {
        // Switching ONE cone on opens the master gate if it is shut. Without this the button did
        // nothing visible - the weapon left the hidden set and the stage pill still vetoed every
        // cone on the model - which reads as a broken control rather than as a gate.
        //
        // Only in this direction. Pressing the master itself is still all-or-nothing, and switching
        // a single cone OFF must not take the gate down with it: the others are still drawn.
        if (on) {
            setFireArcs(true);
        }

        setHiddenWeapons((current) => {
            if (current.has(id) !== on) {
                return current;
            }

            const next = new Set(current);
            if (on) {
                next.delete(id);
            } else {
                next.add(id);
            }

            return next;
        });
    }, []);

    /**
     * Asks the host for everything the geometry now loaded needs in order to be DRAWN.
     *
     * Textures are asked for only once the geometry that samples them exists, so nothing is fetched
     * for a part that failed to load. Effects are fetched once each; a missing one is normal and
     * simply leaves the archetype material in place.
     *
     * Shared by every path that puts geometry in the scene rather than written out at the one that
     * came first. A wreck arriving is the same question as a hardpoint arriving, and the breakoff
     * path having its own early return - and so no textures - was only visible as a black shape.
     */
    const requestMaterialAssets = useCallback(
        (viewport: PreviewViewport): void => {
            for (const name of collectTextureNames(viewport.materialExtras())) {
                assets.requestTexture(name);
            }

            for (const shader of viewport.shaderNames()) {
                assets.requestShader(shader);
            }
        },
        [assets],
    );

    /**
     * Picks the hardpoint a targeting mark stands for, or lets it go.
     *
     * Aims the attacker at it, which is what the marks are FOR - they show what can be shot - and
     * selects its attachment bone so the viewport boxes it too.
     *
     * A second click on the same mark clears both again, exactly as a second click on a tree row or
     * a bone label does. Anything that can be selected has to be UN-selectable from the same
     * control: the first build only ever selected, so once a mark had been clicked there was no way
     * back to an unselected ship without going hunting in the tree.
     */
    const selectHardpoint = useCallback((hardpointId: string): void => {
        const bone = sceneRef.current?.hardpoints.find(
            (h) => h.id === hardpointId,
        )?.attachBone;
        const rowId =
            bone === null || bone === undefined
                ? undefined
                : boneRowIndex(viewportRef.current?.treeItems() ?? []).get(
                      bone.toLowerCase(),
                  );

        setFireTarget((current) => {
            const isSame = current === hardpointId;

            // The bone selection travels WITH the target - one press, one meaning - so it is
            // settled here rather than by a second reducer that could disagree about which hardpoint
            // is current.
            if (isSame) {
                setSelected(new Set());
                setAnchor(null);
            } else if (rowId !== undefined) {
                setSelected(new Set([rowId]));
                setAnchor(rowId);
            }

            return isSame ? "hull" : hardpointId;
        });
    }, []);

    /**
     * Points at a bone from somewhere that is not the tree.
     *
     * Sets exactly what a tree click sets - the row selection that draws the box, and the bone the
     * labels highlight - so a muzzle picked off a weapon row and the same bone picked in the tree
     * are one state rather than two that can disagree.
     */
    const selectBoneRow = useCallback((rowId: string | undefined): void => {
        if (rowId === undefined) {
            return;
        }

        // TOGGLES into the selection rather than replacing it. Replacing meant a weapon with two
        // fire points could never have both boxed at once - press the second and the first let go -
        // and comparing two fire points is the whole reason for pressing either.
        setSelected((current) => {
            const next = toggleSelected(current, rowId);

            setAnchor(next.has(rowId) ? rowId : null);

            return next;
        });
    }, []);

    /**
     * What is wrong with the colour currently applied, if anything.
     *
     * Recomputed rather than stored: it is a pure function of the chosen colour and the faction
     * list, and caching it would only create a second thing that can be stale.
     */
    const colourFindings = useMemo(() => {
        const chosen =
            customColour !== null
                ? parseHex(customColour)
                : (scene?.factions.find((f) => f.name === faction)?.color ??
                  null);

        return chosen === null
            ? []
            : reviewFactionColour(
                  chosen,
                  customColour !== null ? "" : faction,
                  scene?.factions ?? [],
              );
    }, [customColour, faction, scene]);

    /**
     * Switches every system in a group at once.
     *
     * Writes straight through to the systems and keeps nothing: the group's own switch reads back
     * out of them, so this is the only direction state ever moves. The tree refreshes with it,
     * because those same systems each have a row of their own.
     */
    const toggleGroup = useCallback(
        (group: ParticleGroup, visible: boolean): void => {
            for (const id of group.ids) {
                // Through the row chain like every other row, so the effects master and the level
                // gate still get their say and nothing writes visibility behind it. The ROW is asked
                // for: an effect merged into its proxy bone is not addressed as `particle:<id>`.
                viewportRef.current?.applyRowOverrides([
                    {
                        row: viewportRef.current.rowForSystem(id),
                        override:
                            visible === null
                                ? null
                                : visible
                                  ? "shown"
                                  : "hidden",
                    },
                ]);
            }

            setTreeItems(viewportRef.current?.treeItems() ?? []);
        },
        [],
    );

    /**
     * Starting a clip puts the MODEL back first.
     *
     * A full reset, not just the row overrides the viewport already clears: the damage state and the
     * detail level go back to what the model opens at, and whatever the reader picks afterwards
     * layers on top of a known state. Watching an animation is asking whether the animation works,
     * and that question cannot be answered against a model carrying twenty minutes of half-forgotten
     * ticks, a damage level and a detail level.
     *
     * Deliberately only when a clip STARTS. Stopping one leaves the model where the reader left it,
     * because putting it back at that point would undo work they did while watching.
     */
    useEffect(() => {
        if (animation !== null) {
            setAlt(0);
            setLod((current) =>
                levels.lod.length > 0
                    ? levels.lod[levels.lod.length - 1]
                    : current,
            );
            setDestroyed(new Set());
        }

        // A clip the loaded model does not carry must not go on being shown as the one playing.
        // The transport, the playhead and the readout all key off this one string, so a name the
        // mixer refused made the panel report an animation that was never running - which is what
        // an ability's deploy did on every model until `clipFor` stopped handing over a file name.
        // Rare now, and worth keeping: a mod can name a clip its model does not ship.
        //
        // Only once the subject HAS clips, which is the whole guard. A refresh restores the clip
        // that was playing at scene time, before any geometry has arrived - so an unguarded clear
        // would throw away the restored name every time, on the strength of a mixer that had
        // nothing loaded to refuse it with.
        const loaded = viewportRef.current?.animationNames ?? [];
        const started = viewportRef.current?.play(animation) ?? false;

        if (animation !== null && !started && loaded.length > 0) {
            setAnimation(null);
        }

        setTreeItems(viewportRef.current?.treeItems() ?? []);
        // `levels` is read but deliberately NOT a dependency: this fires when the CLIP changes, and
        // adding it would reset the model every time the defined levels were recomputed - which
        // happens on every load and every level change.
    }, [animation]);

    const combat = useCombatLens({
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
    });
    const {
        logOpen,
        destroyed,
        setDestroyed,
        sweeping,
        setSweeping,
        attacker,
        setAttacker,
        attackerPresets,
        setAttackerPresets,
        setProjectileSearch,
        setProjectileNote,
        setAttackerProjectile,
        setEnergyPool,
        hardpointHealth,
        fireTarget,
        setFireTarget,
        hiddenWeapons,
        setHiddenWeapons,
        setTurretAims,
        deadRef,
        weapons,
        sweepable,
        draggableTurrets,
        cards,
        looseWeapons,
    } = combat;

    useHostMessages({
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
    });

    const hardpointList = useHardpointList({
        cards,
        fireTarget,
        hardpointSearchPressed,
        infoCard,
        looseWeapons,
        setInfoCard,
    });

    /** Shield meshes the model names but does not shade as shields. Recomputed with the rows. */
    const shieldOffShader = useMemo(
        // Keyed on the ROWS: they are rebuilt whenever a part loads or the chain re-runs, which is
        // exactly when the answer can change. The viewport ref is deliberately not a dependency -
        // it never changes identity.
        () => viewportRef.current?.shieldMeshesOffShader() ?? [],
        [treeItems],
    );

    /** Which tree row each of the hull's own bones is, for the fire-bone buttons on a row. */
    const boneRows = useMemo(() => boneRowIndex(treeItems), [treeItems]);

    useSubjectSync({
        animation,
        customColour,
        destroyed,
        draggableTurrets,
        faction,
        fireArcs,
        hardpointHealth,
        hiddenWeapons,
        mode,
        reticleState,
        reticlesOn,
        scene,
        scrubbingRef,
        setPlayhead,
        stats,
        sweepable,
        sweeping,
        viewportRef,
        weapons,
    });

    useStoredState({
        alt,
        animation,
        attacker,
        attackerPresets,
        boneFilter,
        collapsed,
        customColour,
        destroyed,
        faction,
        foldedRef,
        hiddenEmitters,
        loading,
        lod,
        restoredRef,
        setCollapsed,
        setTreeItems,
        storedSubjectRef,
        treeItems,
        viewportRef,
    });

    /* The bones the selected ROWS stand for. Derived rather than stored, so a bone cannot be lit
       while its row is not - which is exactly what the old single index kept doing. */
    const selectedBones = useMemo(
        () =>
            new Set(
                [...selected]
                    .map(boneIndexOfRow)
                    .filter((index): index is number => index !== null),
            ),
        [selected],
    );

    // Everything the dock pushes into the viewport. See `useViewportSync`; after the renderer's own
    // effect above, so the viewport starts with every value the panel holds.
    useViewportSync(
        viewportRef,
        {
            grid,
            floor,
            floorLevel,
            wireframe,
            heat: heatOn,
            heatDebug,
            bloom,
            lights,
            wind,
            weatherIntensity,
            background,
            skeleton: skeletonOn,
            labelMode,
            drawDistance,
            selectedBones,
            alt,
            lod,
            particles: particlesOn,
            particlesPaused,
            particleSpeed,
            animation,
            animationSpeed,
            animationLoop,
            animationPaused,
            translated: translatedOn,
        },
        {
            levelsApplied: (viewport) => {
                setStats(viewport.stats());
                setTreeItems(viewport.treeItems());
            },
            translatedApplied: (viewport) =>
                setTranslatedCount(viewport.translatedCoverage()),
        },
    );

    // Depends on `treeItems` as well as on the selection, because the rows a box spans are only
    // known once the tree has been built - a selection restored before the model arrives would
    // otherwise box nothing and look broken.
    useEffect(() => {
        viewportRef.current?.setSelectedRows([...selected]);

        // A selected HARDPOINT outlines its own model rather than its attach bone's subtree. The
        // viewport decides whether it can - the part may not have arrived yet - see `boxTargetFor`.
        const aimed = sceneRef.current?.hardpoints.find(
            (h) => h.id === fireTarget,
        );
        const aimedBone = aimed?.attachBone ?? null;

        viewportRef.current?.setSelectedHardpoint(
            aimed?.partId ?? null,
            aimedBone === null
                ? null
                : (boneRowIndex(viewportRef.current?.treeItems() ?? []).get(
                      aimedBone.toLowerCase(),
                  ) ?? null),
        );
        // fireTarget is in the list because the effect READS it. Left out, the effect closed over
        // whichever hardpoint was aimed at last render - so picking one drew the box for the previous
        // one, or for none at all on the first pick.
    }, [selected, treeItems, fireTarget]);

    /**
     * The skeleton control's three positions, over the two switches underneath.
     *
     * `skeletonOn` and `labelMode` are what the viewport takes, and they stay that way - the
     * viewport has no business knowing this is one control. Off is the only reading that needs a
     * choice made for it: leaving `labelMode` at whatever it was means turning the skeleton back
     * on restores the labelling you had, which is what you meant by turning it off.
     */
    const skeletonMode: "off" | "selected" | "always" = !skeletonOn
        ? "off"
        : labelMode === "all"
          ? "always"
          : "selected";

    const setSkeletonMode = (next: "off" | "selected" | "always"): void => {
        setSkeletonOn(next !== "off");

        if (next !== "off") {
            setLabelMode(next === "always" ? "all" : "selected");
        }
    };

    const noticeState = useNotices({
        anyShaderSource,
        attachmentIssues,
        colourFindings,
        problems,
        problemsOpen,
        shaderRequested,
        shieldOffShader,
        translatedOn,
    });
    const { notices, severity, problemsTitle } = noticeState;
    const handoff =
        scene === null ? null : handoffFor(scene.kind, scene.sourceFile);

    const pills = useOverlayPills({
        emitters,
        fireArcs,
        floor,
        grid,
        groups,
        hiddenWeapons,
        mode,
        particlesOn,
        reticleState,
        reticlesOn,
        scene,
        setFireArcs,
        setFloor,
        setGrid,
        setHiddenWeapons,
        setParticlesOn,
        setReticlesOn,
        setWireframe,
        weapons,
        wireframe,
    });
    const { overlays } = pills;

    return (
        <Shell>
            <div className="body">
                <div className="stage-column">
                    <div className="viewport">
                        <canvas ref={canvasRef} />
                        <div className="bone-labels" ref={labelsRef} />

                        {/* The area a capture writes, while the capture settings are open: the
                            largest box of the capture's shape that fits, centred, with the rest
                            dimmed - the same arithmetic the capture renders with (captureFrame). */}
                        {cameraOpen &&
                            !folded.has("camera.capture") &&
                            captureDims !== null && (
                                <CaptureFrame captureDims={captureDims} />
                            )}

                        {/* Over the canvas while the subject assembles itself.

                            The parts arrive one at a time and a part's textures cannot be asked for
                            until its geometry exists, so what a reader saw was a black hull, then
                            black turrets, then textures landing one by one. All of it is the load
                            order showing through, and none of it is worth watching. The counts are
                            here because a cover that says nothing turns a slow load into a hang. */}
                        {loading.covered && <LoadCover loading={loading} />}

                        {/* The SCENE's settings, not the model's - so they sit on the viewport
                            rather than off the dock that describes the subject. A globe in the
                            stage's own corner says "this is about the room you are looking into";
                            the same controls in the right-hand dock read as properties of the
                            model, and buried three quarters of that dock while open. */}
                        <StageTopEdge
                            authorCameras={authorCameras}
                            cameraOpen={cameraOpen}
                            cameraView={cameraView}
                            cameraViews={cameraViews}
                            chooseTint={chooseTint}
                            customColour={customColour}
                            faction={faction}
                            overlays={overlays}
                            scene={scene}
                            setCameraOpen={setCameraOpen}
                            setCameraView={setCameraView}
                            setWorldOpen={setWorldOpen}
                            viewportRef={viewportRef}
                            worldOpen={worldOpen}
                        />

                        {worldOpen && (
                            <SceneFlyout
                                anyShaderSource={anyShaderSource}
                                cameraState={cameraState}
                                editing={editing}
                                flyouts={flyouts}
                                ground={ground}
                                groundDragging={groundDragging}
                                hasWeather={hasWeather}
                                room={room}
                                scene={scene}
                                setEditing={setEditing}
                                setLight={setLight}
                                shadowReach={shadowReach}
                                toggleSection={toggleSection}
                                translatedCount={translatedCount}
                            />
                        )}

                        {/* Where you look FROM. The mirror of the scene panel opposite, down to the
                            corner it hangs in and the reset in its head - the two are the same kind
                            of thing about different halves of the picture. */}
                        {cameraOpen && (
                            <CameraFlyout
                                cameraState={cameraState}
                                captureDims={captureDims}
                                captureScenery={captureScenery}
                                captureSizeId={captureSizeId}
                                captureTransparent={captureTransparent}
                                customCapture={customCapture}
                                flyouts={flyouts}
                                renamingPreset={renamingPreset}
                                room={room}
                                scene={scene}
                                sceneRef={sceneRef}
                                setCaptureScenery={setCaptureScenery}
                                setCaptureSizeId={setCaptureSizeId}
                                setCaptureTransparent={setCaptureTransparent}
                                setCustomCapture={setCustomCapture}
                                setRenamingPreset={setRenamingPreset}
                                toggleSection={toggleSection}
                                viewportRef={viewportRef}
                            />
                        )}

                        {/* The bottom edge: what the subject is DOING.

                            Three slots like the top edge, and for the reason recorded on
                            `.stage-slot` - two equal side slots are the whole of what centres the
                            middle one. It carried two absolutely-positioned plates before, which
                            was enough for two and is not enough for four.

                            The renderer used to sit here and does not any more: it is a standing
                            choice about how the PICTURE is drawn, not about what the subject is
                            doing, so it lives in Scene > Effects beside heat and bloom. What is
                            left on this edge is the subject's own state. */}
                        <StageSlots
                            edge="bottom"
                            start={
                                <SubjectStateControls
                                    alt={alt}
                                    altSteps={altSteps}
                                    combat={combat}
                                    levels={levels}
                                    lod={lod}
                                    lodChosenRef={lodChosenRef}
                                    lodCost={lodCost}
                                    lodSteps={lodSteps}
                                    scene={scene}
                                    setAlt={setAlt}
                                    setLod={setLod}
                                />
                            }
                            middle={
                                <AbilityCommandBar
                                    abilitiesOpen={abilitiesOpen}
                                    abilityState={abilityState}
                                    combat={combat}
                                    mode={mode}
                                    scene={scene}
                                    setAbilitiesOpen={setAbilitiesOpen}
                                />
                            }
                            end={
                                <AttackControls
                                    attackOpen={attackOpen}
                                    combat={combat}
                                    mode={mode}
                                    scene={scene}
                                    setAttackOpen={setAttackOpen}
                                />
                            }
                        />
                        {/* What the command bar cannot say in an icon.

                            One row per ability: the art, the words the game shows, what it drives
                            on THIS model, and the jump to where it is defined. No switch - the bar
                            above owns that, and offering it twice would be two controls for one
                            fact. */}
                        {abilitiesOpen && (
                            <AbilitiesFlyout
                                abilitiesRef={abilitiesRef}
                                abilityState={abilityState}
                                infoAbility={infoAbility}
                                setAbilitiesOpen={setAbilitiesOpen}
                                setInfoAbility={setInfoAbility}
                            />
                        )}

                        {/* What each shot DID, newest first - the reader asked for it in the shape
                            an old-school RPG reports a hit. The bars say what is left; this says
                            how it got that way, and the armour on every line is what answers "why
                            that number".

                            Its own flyout beside the settings, not a panel inside them: a log is
                            read after the fact and the settings are set before it. */}
                        {logOpen && (
                            <DamageLogFlyout combat={combat} scene={scene} />
                        )}

                        {/* The panel is INVERTED against the rest of the lens: everything else
                            here describes the subject, and this describes a weapon you build to
                            shoot it with. The heading says so, because a reader who assumes these
                            are the ship's own numbers would read every field backwards. */}
                        {attackOpen && (
                            <WeaponFlyout
                                combat={combat}
                                flyouts={flyouts}
                                mode={mode}
                                scene={scene}
                            />
                        )}
                    </div>

                    {problemsOpen && notices.length > 0 && (
                        <PreviewProblems
                            notices={notices}
                            setProblemsOpen={setProblemsOpen}
                        />
                    )}
                </div>

                <RightDock
                    memoKey="modelPreview.dock"
                    headerHoldsDial
                    initialWidth={260}
                    minWidth={200}
                    maxWidth={460}
                    header={
                        <DockHeader
                            abilityState={abilityState}
                            flyouts={flyouts}
                            handoff={handoff}
                            mode={mode}
                            modelDetail={modelDetail}
                            notices={notices}
                            player={player}
                            problemsOpen={problemsOpen}
                            problemsTitle={problemsTitle}
                            scene={scene}
                            setMode={setMode}
                            setProblemsOpen={setProblemsOpen}
                            severity={severity}
                            stats={stats}
                        />
                    }
                    content={
                        <DockContent
                            abilityState={abilityState}
                            anchor={anchor}
                            boneRows={boneRows}
                            clearSelection={clearSelection}
                            combat={combat}
                            detailsRow={detailsRow}
                            emitters={emitters}
                            flyouts={flyouts}
                            groups={groups}
                            hardpointList={hardpointList}
                            hiddenEmitters={hiddenEmitters}
                            inspection={inspection}
                            mode={mode}
                            particles={particles}
                            player={player}
                            scene={scene}
                            selectBoneRow={selectBoneRow}
                            selectHardpoint={selectHardpoint}
                            selected={selected}
                            setAnchor={setAnchor}
                            setSelected={setSelected}
                            setWeaponArcs={setWeaponArcs}
                            toggleGroup={toggleGroup}
                            tree={tree}
                            treeItems={treeItems}
                            viewportRef={viewportRef}
                        />
                    }
                    overview={
                        <DockFoot
                            abilityState={abilityState}
                            combat={combat}
                            mode={mode}
                            player={player}
                            scene={scene}
                            setMode={setMode}
                            setSkeletonMode={setSkeletonMode}
                            skeletonMode={skeletonMode}
                            viewportRef={viewportRef}
                        />
                    }
                />

                {/* What one row IS now opens as its own editor tab beside this one - see
                    `openInspector` and `modelInspectorPanel.ts`. It was a 320px flyout holding a
                    mesh's sections AND ten columns of vertex data, which is a page's worth of
                    reading in a fifth of the screen. The two flyouts below stayed: an ability and a
                    hardpoint card are a handful of rows, and they belong beside the thing that
                    named them. */}
                {/* What the FILE says about an ability, beside the card that names it. The card's
                    face carries what the GAME says; this is the other half. */}
                {infoAbility !== null && (
                    <AbilityInfoFlyout
                        abilityInfoAt={abilityInfoAt}
                        abilityInfoRef={abilityInfoRef}
                        abilityState={abilityState}
                        infoAbility={infoAbility}
                        setInfoAbility={setInfoAbility}
                    />
                )}

                {/* What a hardpoint measures. Its own flyout rather than an unfold inside the
                    card: an unfold pushes every card below it down the list, and this panel says
                    everything else of this shape - a tree row's details, the scene, the camera -
                    in a flyout. */}
                {infoCard !== null && (
                    <CardInfoFlyout
                        cardInfoAt={cardInfoAt}
                        cardInfoRef={cardInfoRef}
                        combat={combat}
                        infoCard={infoCard}
                        setInfoCard={setInfoCard}
                    />
                )}
            </div>
        </Shell>
    );
}

createRoot(document.getElementById("root")!).render(<ModelPreview />);
