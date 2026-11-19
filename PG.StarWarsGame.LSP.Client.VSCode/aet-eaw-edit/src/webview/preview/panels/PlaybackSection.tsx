// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The player: play, step and seek through the clip, its speed and its loop.

import type { RefObject } from "react";
import type { AnimationTransport } from "../useAnimationTransport";
import * as l10n from "@vscode/l10n";
import { IconButton } from "../../shared/Button";
import { TransportBar } from "../../shared/TransportBar";
import { type PreviewViewport } from "../viewport";
import { playheadLabel } from "../animationNames";

export interface PlaybackSectionProps {
    player: AnimationTransport;
    viewportRef: RefObject<PreviewViewport | null>;
}

export function PlaybackSection({
    player,
    viewportRef,
}: PlaybackSectionProps): React.JSX.Element {
    return (
        <div className="dock-section player-section">
            <div className="dock-section-title">
                {l10n.t("Playback")}
                {/* The ACTION and the take, which is what the reader picked - the
                                    filename repeats the model's name back at them. A rouletting
                                    action says so, because the clip under the playhead is about to
                                    change on its own and a readout that did not mention it would
                                    look like a bug. */}
                <span className="section-count">
                    {player.animation === null
                        ? l10n.t("no clip")
                        : playheadLabel(
                              player.actionOfClip.get(player.animation) ?? null,
                              player.animation,
                              player.roulette !== null,
                          )}
                </span>
            </div>

            {/* The layout every media player has had for thirty years, because
                                it needs no explaining: transport on the left, time on the right,
                                the scrub bar across the bottom. */}
            <TransportBar
                controls={
                    <>
                        {/* The order and the glyphs a CD player uses, which is the
                                    reader's own reference: |<< to the previous clip, |< to this
                                    clip's first frame, play, >| to its last frame, >>| to the next
                                    clip, then the repeat latch. Last frame used to draw a bare play
                                    triangle - the same glyph as Play, saying the opposite of what
                                    it did. */}
                        {/* Every one of these says why it cannot be pressed. Four of
                                        them used to go quiet instead - "Previous clip" on a control
                                        that could not step - and only Play had ever been given the
                                        sentence. Its wording is reused rather than reinvented: it
                                        is already this panel's way of saying the same state. */}
                        <IconButton
                            icon="previousClip"
                            title={l10n.t("Previous clip")}
                            disabled={player.orderedClips.length < 2}
                            disabledReason={l10n.t("Only one clip")}
                            onClick={() => player.step(-1)}
                        />
                        <IconButton
                            icon="firstFrame"
                            title={l10n.t("First frame")}
                            disabled={player.animation === null}
                            disabledReason={l10n.t(
                                "Pick a clip below to play it",
                            )}
                            onClick={() => player.seek(0)}
                        />
                        <IconButton
                            icon={player.playing ? "pause" : "play"}
                            title={
                                player.playing
                                    ? l10n.t("Pause")
                                    : player.atEnd && !player.animationLoop
                                      ? l10n.t(
                                            "Play again from the first frame",
                                        )
                                      : l10n.t("Play")
                            }
                            disabled={player.animation === null}
                            disabledReason={l10n.t(
                                "Pick a clip below to play it",
                            )}
                            onClick={player.togglePlay}
                        />
                        <IconButton
                            icon="lastFrame"
                            title={l10n.t("Last frame")}
                            disabled={player.animation === null}
                            disabledReason={l10n.t(
                                "Pick a clip below to play it",
                            )}
                            onClick={() =>
                                player.seek(player.playhead.duration)
                            }
                        />
                        <IconButton
                            icon="nextClip"
                            title={l10n.t("Next clip")}
                            disabled={player.orderedClips.length < 2}
                            disabledReason={l10n.t("Only one clip")}
                            onClick={() => player.step(1)}
                        />
                        <IconButton
                            icon="loop"
                            className={
                                player.animationLoop ? "active" : undefined
                            }
                            pressed={player.animationLoop}
                            title={
                                player.animationLoop
                                    ? l10n.t(
                                          "Looping. Press to play the clip once and hold the frame it ends on.",
                                      )
                                    : l10n.t(
                                          "Playing once and holding the frame it ends on. Press to loop.",
                                      )
                            }
                            disabled={player.animation === null}
                            disabledReason={l10n.t(
                                "Pick a clip below to play it",
                            )}
                            onClick={() =>
                                player.setAnimationLoop((current) => !current)
                            }
                        />
                    </>
                }
                clock={l10n.t(
                    "{0}{1}{2}s",
                    player.playhead.time.toFixed(2),
                    " / ",
                    player.playhead.duration.toFixed(2),
                )}
                middle={
                    <input
                        className="player-scrub"
                        type="range"
                        disabled={player.animation === null}
                        min={0}
                        max={Math.max(player.playhead.duration, 0.001)}
                        step={0.001}
                        value={player.playhead.time}
                        title={l10n.t("Drag to a frame")}
                        onPointerDown={() => {
                            player.scrubbingRef.current = true;
                        }}
                        onPointerUp={() => {
                            player.scrubbingRef.current = false;
                        }}
                        onChange={(e) => {
                            const time = Number(e.target.value);
                            viewportRef.current?.seekAnimation(time);
                            player.setPlayhead((current) => ({
                                ...current,
                                time,
                            }));
                        }}
                    />
                }
                speed={{
                    readout: l10n.t("{0}x", player.animationSpeed.toFixed(2)),
                    slider: (
                        <input
                            type="range"
                            disabled={player.animation === null}
                            min={0}
                            max={2}
                            step={0.05}
                            value={player.animationSpeed}
                            title={l10n.t("Playback speed")}
                            onChange={(e) =>
                                player.setAnimationSpeed(Number(e.target.value))
                            }
                        />
                    ),
                }}
            />
        </div>
    );
}
