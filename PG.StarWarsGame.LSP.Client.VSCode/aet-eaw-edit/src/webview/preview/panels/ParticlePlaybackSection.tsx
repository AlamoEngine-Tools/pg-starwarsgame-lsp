// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// A particle file's player (#289): play, pause, stop and restart over its systems, and their speed.
// The animation player's TransportBar in the animation player's place, the dock foot - a particle
// file has no clips, so the two never share a dock.

import * as l10n from "@vscode/l10n";
import { IconButton } from "../../shared/Button";
import { TransportBar } from "../../shared/TransportBar";
import type { ParticleTransport } from "../particleTransport";

export interface ParticlePlaybackSectionProps {
    transport: ParticleTransport;
    /** Whether a system has arrived to drive. */
    ready: boolean;
    /** The particle speed, a room setting: 1 is real time. */
    speed: number;
    setSpeed: (speed: number) => void;
}

export function ParticlePlaybackSection({
    transport,
    ready,
    speed,
    setSpeed,
}: ParticlePlaybackSectionProps): React.JSX.Element {
    const notReady = l10n.t("No particle system loaded");
    const { run } = transport;

    return (
        <div className="dock-section player-section">
            <div className="dock-section-title">{l10n.t("Playback")}</div>

            {/* No clock and no scrubber: a particle system has no timeline to read or drag. */}
            <TransportBar
                className="particle-transport"
                controls={
                    <>
                        {/* Play after Stop starts from nothing, so it says so rather than
                            claiming to carry on. */}
                        <IconButton
                            icon={run === "playing" ? "pause" : "play"}
                            title={
                                run === "playing"
                                    ? l10n.t("Pause")
                                    : run === "stopped"
                                      ? l10n.t("Play from the start")
                                      : l10n.t("Play")
                            }
                            disabled={!ready}
                            disabledReason={notReady}
                            onClick={transport.togglePlay}
                        />
                        <IconButton
                            icon="stop"
                            title={l10n.t("Stop emitting")}
                            disabled={!ready || run === "stopped"}
                            disabledReason={
                                ready ? l10n.t("Already stopped") : notReady
                            }
                            onClick={transport.stop}
                        />
                        <IconButton
                            icon="restart"
                            title={l10n.t("Restart")}
                            disabled={!ready}
                            disabledReason={notReady}
                            onClick={transport.restart}
                        />
                    </>
                }
                speed={{
                    readout: l10n.t("{0}x", speed.toFixed(2)),
                    // The animation player's range and step, so the two speeds read alike.
                    slider: (
                        <input
                            type="range"
                            disabled={!ready}
                            min={0}
                            max={2}
                            step={0.05}
                            value={speed}
                            title={l10n.t("Particle speed")}
                            onChange={(e) => setSpeed(Number(e.target.value))}
                        />
                    ),
                }}
            />
        </div>
    );
}
