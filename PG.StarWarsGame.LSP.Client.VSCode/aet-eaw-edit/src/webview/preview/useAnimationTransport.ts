// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The animation transport: the clip playing, its speed, loop and playhead, the library it is picked
// from, and the buttons that play, step and seek. One hook so the Animations section and the player
// read one object.

import {
    useCallback,
    useEffect,
    useMemo,
    useRef,
    useState,
    type RefObject,
} from "react";
import { type PreviewScene } from "../../protocol/modelPreview";
import { type PreviewViewport, type ViewportStats } from "./viewport";
import {
    type AnimationAction,
    clipNamingModel,
    groupAnimations,
} from "./animationNames";
import { pickTake } from "./takeRoulette";

/** What useAnimationTransport reads from the rest of the preview. */
export interface AnimationTransportInputs {
    scene: PreviewScene | null;
    stats: ViewportStats | null;
    viewportRef: RefObject<PreviewViewport | null>;
}

export type AnimationTransport = ReturnType<typeof useAnimationTransport>;

/** What pressing a card or a take does: see {@link cardPress}. */
export type CardPress = "play" | "pause" | "resume" | "replay";

/**
 * What pressing a card - or one take, with `clips` holding just that take - does.
 *
 * A card whose clip is not the current one plays. The current clip pauses while it runs, resumes
 * where it was paused, and plays again from the start once it has ended. A card used to play
 * whenever it was pressed, so pressing the current one set the name already set: nothing re-ran,
 * and a clip that plays once could only be started again from the scrubber (#440).
 */
export function cardPress(
    clips: readonly string[],
    current: string | null,
    playhead: { running: boolean; atEnd: boolean },
): CardPress {
    if (current === null || !clips.includes(current)) {
        return "play";
    }

    if (playhead.running) {
        return "pause";
    }

    return playhead.atEnd ? "replay" : "resume";
}

export function useAnimationTransport({
    scene,
    stats,
    viewportRef,
}: AnimationTransportInputs) {
    const [animation, setAnimation] = useState<string | null>(null);

    /**
     * Counts every start asked for by hand, so starting the clip that is already current starts it
     * again: the name alone does not change, and the effect that plays the clip keys on both.
     */
    const [clipStart, setClipStart] = useState(0);

    /**
     * The transport.
     *
     * Speed is a multiple of the clip's OWN rate, which the exporter bakes from the `.ala` header -
     * 30 fps for 1270 of the 1363 shipped animations, and 1, 5, 10 or 15 for the rest. So 1.0 is
     * already what the game does, and there is no default frame rate for the panel to impose.
     *
     * Looping defaults on because most of what a modder checks is a cycle - idles, walks, turns -
     * and a walk that plays once and stops cannot be judged at all.
     */
    const [animationPaused, setAnimationPaused] = useState(false);
    const [animationSpeed, setAnimationSpeed] = useState(1);
    const [animationLoop, setAnimationLoop] = useState(true);

    /** Where the playhead is. Polled from the mixer, which is the only thing that knows. */
    const [playhead, setPlayhead] = useState({
        time: 0,
        duration: 0,
        running: false,
    });

    /**
     * True while the scrubber is held.
     *
     * The poll below would otherwise fight the drag: it writes the mixer's time into the slider
     * sixty times a second, so a slider the reader is dragging snapped back under their finger.
     */
    const scrubbingRef = useRef(false);

    const animations = stats?.animations ?? [];

    /**
     * The model the clips are named after - see `clipNamingModel`.
     *
     * Never the subject, which for a game object is its XML name (`Generic_Star_Destroyer`), and not
     * always the hull either: a unit borrowing another model's animation set has clips carrying the
     * SOURCE model's name.
     */
    const animationModel = useMemo(() => clipNamingModel(scene), [scene]);

    const animationGroups = useMemo(
        () => groupAnimations(animationModel, animations),
        [animationModel, animations],
    );

    /**
     * Every clip in the order the library shows them, which is the order the track buttons step
     * through. Grouped order, not file order - stepping past the end of Idle should land on the
     * first Movement clip, the way the eye reads the list.
     */
    const orderedClips = animationGroups.flatMap((group) =>
        group.actions.flatMap((action) =>
            action.takes.map((take) => take.name),
        ),
    );
    const clipAt = animation === null ? -1 : orderedClips.indexOf(animation);

    /** Which action each clip belongs to, so a clip picked any way at all can find its loop rule. */
    const actionOfClip = useMemo(() => {
        const byClip = new Map<string, AnimationAction>();

        for (const group of animationGroups) {
            for (const action of group.actions) {
                for (const take of action.takes) {
                    byClip.set(take.name, action);
                }
            }
        }

        return byClip;
    }, [animationGroups]);

    /**
     * Repeat follows the CLIP, not the reader.
     *
     * An idle is meant to run on a loop and a death is meant to end; opening every clip looping was
     * right for one of those and wrong for the other, and the reader had to notice and correct it
     * each time. `animationNames` decides which is which - see its loop rule. Only when the clip
     * CHANGES, so pressing the latch during a clip still holds.
     */
    useEffect(() => {
        if (animation === null) {
            return;
        }

        setAnimationLoop(actionOfClip.get(animation)?.loops ?? false);
    }, [animation, actionOfClip]);

    /**
     * The action whose takes are being drawn at random, or null when a clip was picked by hand.
     *
     * STATE, not a ref. The playhead readout says "random" while this is set, and a ref read during
     * render only happens to be right when something else re-renders in the same breath - which is
     * exactly the bug the probe caught: picking a take by hand cleared the ref and the readout went
     * on saying random.
     */
    const [roulette, setRoulette] = useState<AnimationAction | null>(null);

    /** Draws a take of one action and plays it. The game's own behaviour - see `takeRoulette`. */
    const playAction = useCallback((action: AnimationAction): void => {
        // Only where there is something to draw from. A one-take action played from its tile is
        // just that clip, and saying "random" over it would be a claim about nothing.
        setRoulette(action.takes.length > 1 ? action : null);
        setAnimation(pickTake(action.takes, Math.random()).name);
        setClipStart((count) => count + 1);
        setAnimationPaused(false);
    }, []);

    /** Picking a named take by hand stops the roulette - the reader asked for that recording. */
    const pickClip = useCallback((clip: string): void => {
        setRoulette(null);
        setAnimation(clip);
        setClipStart((count) => count + 1);
        setAnimationPaused(false);
    }, []);

    /**
     * Draws again each time a rouletting clip comes round.
     *
     * The wrap is read off the playhead going BACKWARDS, which is the only thing on this side that
     * knows a looping clip restarted - the mixer does it internally and announces nothing. Watched
     * at the same 50ms the scrubber already ticks at, so it costs no extra timer.
     */
    const lastTimeRef = useRef(0);

    useEffect(() => {
        const wrapped = playhead.time < lastTimeRef.current - 1e-4;

        lastTimeRef.current = playhead.time;

        if (roulette !== null && animationLoop && wrapped) {
            setAnimation(pickTake(roulette.takes, Math.random()).name);
        }
    }, [playhead.time, animationLoop, roulette]);

    /** Families the reader has folded away, by family id. */
    const [foldedFamilies, setFoldedFamilies] = useState<ReadonlySet<string>>(
        new Set(),
    );

    const toggleFamily = (family: string): void =>
        setFoldedFamilies((current) => {
            const next = new Set(current);
            if (!next.delete(family)) {
                next.add(family);
            }
            return next;
        });

    /**
     * Whether the clip is actually advancing, taken from the mixer rather than from the panel.
     *
     * A clip that runs off its end without looping is stopped, but nothing told the panel: the
     * pause button went on showing a pause glyph over a model standing perfectly still, and
     * pressing it "paused" something that had already finished. The mixer knows; the panel asks.
     */
    const playing = playhead.running;
    const atEnd =
        playhead.duration > 0 && playhead.time >= playhead.duration - 1e-4;

    /** Play, resume, or replay - a media player's one button does all three. */
    const togglePlay = (): void => {
        if (playing) {
            setAnimationPaused(true);
            return;
        }

        // Pressing play on a finished clip replays it. Resuming from the last frame would finish
        // again the same instant, which reads as a dead button.
        if (atEnd && !animationLoop) {
            viewportRef.current?.seekAnimation(0);
            setPlayhead((current) => ({ ...current, time: 0 }));
        }

        // Straight at the viewport, not only through the state. A clip that ran off its end was
        // paused by the MIXER, not by the reader, so `animationPaused` was already false - setting
        // it to false again changes nothing, runs no effect, and the button does nothing at all.
        viewportRef.current?.setPaused(false);
        setAnimationPaused(false);
    };

    /**
     * A card or a take pressed: plays it, or pauses, resumes or replays the current clip - see
     * {@link cardPress}.
     *
     * Resuming goes straight at the viewport as well, for the reason `togglePlay` gives.
     */
    const press = (clips: readonly string[], start: () => void): void => {
        switch (cardPress(clips, animation, { running: playing, atEnd })) {
            case "pause":
                setAnimationPaused(true);
                return;
            case "resume":
                viewportRef.current?.setPaused(false);
                setAnimationPaused(false);
                return;
            default:
                start();
        }
    };

    const pressAction = (action: AnimationAction): void =>
        press(
            action.takes.map((take) => take.name),
            () => playAction(action),
        );

    const pressTake = (clip: string): void =>
        press([clip], () => pickClip(clip));

    const step = (by: number): void => {
        if (orderedClips.length === 0) {
            return;
        }

        // Wraps, so the buttons never dead-end at the top or bottom of the list.
        const next = (clipAt + by + orderedClips.length) % orderedClips.length;
        setAnimation(
            orderedClips[
                clipAt < 0 ? (by > 0 ? 0 : orderedClips.length - 1) : next
            ],
        );
        setAnimationPaused(false);
    };

    const seek = (seconds: number): void => {
        viewportRef.current?.seekAnimation(seconds);
        setPlayhead((current) => ({ ...current, time: seconds }));
    };

    return {
        animation,
        setAnimation,
        clipStart,
        animationPaused,
        animationSpeed,
        setAnimationSpeed,
        animationLoop,
        setAnimationLoop,
        playhead,
        setPlayhead,
        scrubbingRef,
        animations,
        animationGroups,
        orderedClips,
        actionOfClip,
        roulette,
        playAction,
        pickClip,
        pressAction,
        pressTake,
        foldedFamilies,
        toggleFamily,
        playing,
        atEnd,
        togglePlay,
        step,
        seek,
    };
}
