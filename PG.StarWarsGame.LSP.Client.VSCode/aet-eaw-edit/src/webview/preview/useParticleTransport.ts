// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The particle transport's state, for a particle file's dock (#289). The rules are
// `particleStep`'s; this holds the run, carries a verb's one call to the viewport, and starts over
// as playing whenever a scene arrives. The run belongs to the subject, not the viewer, and every
// scene - a refresh of the same file too - clears the viewport, which rebuilds the systems
// unheld; a run left at Stop would then be a bar contradicting the stage.

import { type RefObject, useEffect, useRef, useState } from "react";

import {
    type ParticleRun,
    type ParticleTransport,
    type ParticleVerb,
    particleStep,
} from "./particleTransport";
import { type PreviewViewport } from "./viewport";

export function useParticleTransport(
    viewportRef: RefObject<PreviewViewport | null>,
    /** The scene on screen; each new one resets the run. */
    scene: object | null,
): ParticleTransport {
    const [run, setRun] = useState<ParticleRun>("playing");
    // Read by the verbs, so two presses inside one render each see the run the first left.
    const runRef = useRef(run);

    useEffect(() => {
        runRef.current = "playing";
        setRun("playing");
    }, [scene]);

    const apply = (verb: ParticleVerb): void => {
        const step = particleStep(runRef.current, verb);

        if (step.viewport === "stop") {
            viewportRef.current?.stopParticles();
        } else if (step.viewport === "restart") {
            viewportRef.current?.restartParticles();
        }

        runRef.current = step.run;
        setRun(step.run);
    };

    return {
        run,
        togglePlay: () =>
            apply(runRef.current === "playing" ? "pause" : "play"),
        stop: () => apply("stop"),
        restart: () => apply("restart"),
    };
}
