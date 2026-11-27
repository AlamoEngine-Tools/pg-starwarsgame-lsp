// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// A zoom as a slider with the zoom in percent beside it, in a dock's foot.
//
// Shared by the story graph's canvas and the encyclopedia's card, which zoomed one with nothing on
// screen and the other under a label reading "Zoom 3.00x". The styles are `zoomSliderCss`.

import * as l10n from "@vscode/l10n";

/** The ends of a logarithmic track. */
export interface ZoomRange {
    min: number;
    max: number;
}

/** How many steps a logarithmic track has. */
const LOG_STEPS = 1000;

/**
 * Where a zoom sits on a logarithmic track, from 0 to 1000. Each doubling takes the same length; a
 * zoom outside the range is held at the end it passed.
 */
export function sliderOf(zoom: number, range: ZoomRange): number {
    const clamped = Math.min(range.max, Math.max(range.min, zoom));
    const at = Math.log(clamped / range.min) / Math.log(range.max / range.min);
    return Math.round(at * LOG_STEPS);
}

/** The zoom at a place on a logarithmic track. */
export function zoomOf(slider: number, range: ZoomRange): number {
    return range.min * Math.pow(range.max / range.min, slider / LOG_STEPS);
}

export interface ZoomSliderProps {
    /** The zoom, as a factor: 1 is 100%. */
    value: number;
    min: number;
    max: number;
    /** The step of a linear track. A logarithmic one has a thousand. */
    step?: number;
    /**
     * Lays the track out by doublings. For a range of two orders of magnitude, where a linear
     * track gives everything below 20% the first few pixels.
     */
    logarithmic?: boolean;
    /** What the slider is called, for a screen reader. */
    label: string;
    title?: string;
    onZoom: (zoom: number) => void;
}

export function ZoomSlider(props: ZoomSliderProps): React.JSX.Element {
    const range = { min: props.min, max: props.max };
    const log = props.logarithmic ?? false;

    return (
        <div className="zoom-slider">
            <input
                type="range"
                aria-label={props.label}
                title={props.title}
                min={log ? 0 : props.min}
                max={log ? LOG_STEPS : props.max}
                step={log ? 1 : props.step}
                value={log ? sliderOf(props.value, range) : props.value}
                onChange={(event) => {
                    const at = Number(event.target.value);
                    props.onZoom(log ? zoomOf(at, range) : at);
                }}
            />
            {/* What the zoom is, whatever moved it - the slider, a wheel, a fit. */}
            <span className="zoom-readout">
                {l10n.t("{0}%", Math.round(props.value * 100))}
            </span>
        </div>
    );
}
