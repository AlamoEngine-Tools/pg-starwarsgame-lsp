// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Everything a colour or a shape on the story graph means, in one flyout (issue #128).
//
// It replaces the legend strip under the canvas. That strip keyed lifecycle and edge kind, but the
// colours the reporter could not read were BRANCH colours - a zoomed-out node is drawn in its
// branch's hue, and which hue a branch gets depends on its place in the campaign's branch list. A
// fixed strip cannot key that, so this is built from the branches in the graph.
//
// Recolouring the overview by lifecycle and carrying the branch as a glow or outline was ruled out:
// that is what cost the graph its frame rate before. A key costs nothing while it is closed.
//
// The shell is the model preview's stage flyout (`stageFlyoutCss`), on purpose: the same panel,
// opened the same way, from the same kind of corner plate.

import { IconButton } from '../shared/Button';
import { DockSection } from '../shared/DockSection';

import type { BranchKeyEntry } from './colourKey';
import { EDGE_KINDS, LIFECYCLE_TOKENS, UNKNOWN_LIFECYCLE_TOKEN } from './palette';

export interface ColourKeyFlyoutProps {
    branches: readonly BranchKeyEntry[];
    onClose: () => void;
}

export function ColourKeyFlyout({ branches, onClose }: ColourKeyFlyoutProps): React.JSX.Element {
    return (
        <div className="stage-flyout from-bottom on-right colour-key" role="dialog" aria-label="Colour key">
            <div className="stage-flyout-head">
                Colour key
                <IconButton icon="close" title="Close" onClick={onClose} />
            </div>
            <div className="stage-flyout-body">
                <DockSection title="Nodes">
                    <ul className="key-list">
                        {/* Generated from the same mapping the node borders are, so a swatch cannot
                            come to disagree with the node it describes. */}
                        <li>
                            <span className="swatch" style={{ borderColor: `var(${UNKNOWN_LIFECYCLE_TOKEN})` }} />
                            <span className="key-label">Inactive</span>
                        </li>
                        {Object.entries(LIFECYCLE_TOKENS).map(([lifecycle, token]) => (
                            <li key={lifecycle}>
                                <span className="swatch" style={{ borderColor: `var(${token})` }} />
                                <span className="key-label">{lifecycle}</span>
                            </li>
                        ))}
                        <li><span className="shape-diamond" /><span className="key-label">OR</span></li>
                        <li><span className="shape-circle" /><span className="key-label">AND</span></li>
                    </ul>
                </DockSection>

                <DockSection title="Edges">
                    <ul className="key-list">
                        {/* Real strokes from EDGE_KINDS - same token, same dash array as the graph,
                            because the dash is part of what a kind means. */}
                        {EDGE_KINDS.map(kind => (
                            <li key={kind.kind}>
                                <svg className="edge-swatch" width="22" height="6" aria-hidden="true">
                                    <line
                                        x1="0" y1="3" x2="22" y2="3"
                                        stroke={`var(${kind.token})`}
                                        strokeWidth="2"
                                        strokeDasharray={kind.dash || undefined}
                                    />
                                </svg>
                                <span className="key-label">{kind.label}</span>
                            </li>
                        ))}
                    </ul>
                    <p className="field-note">Drag socket to socket to add a Requires edge</p>
                </DockSection>

                <DockSection title="Branches" count={branches.length}>
                    {/* The branch hues are the lifecycle hues, so this has to say which one wins. */}
                    <p className="field-note">
                        Zoomed out, a node in a branch is drawn in the branch colour instead of its
                        lifecycle. The glow round a node and the Requires edges into it take the
                        branch colour too.
                    </p>
                    {branches.length === 0 ? (
                        <p className="field-note">No event in this view names a branch</p>
                    ) : (
                        <ul className="key-list">
                            {branches.map(entry => (
                                <li key={entry.branch} data-branch={entry.branch}>
                                    {/* Fill and stroke as the overview draws the node. */}
                                    <span
                                        className="swatch branch-swatch"
                                        style={{
                                            borderColor: `var(${entry.token})`,
                                            background: `color-mix(in srgb, var(${entry.token}) 28%, transparent)`,
                                        }}
                                    />
                                    <span className="key-label">{entry.branch}</span>
                                    {entry.sharedWith.length > 0 && (
                                        <span className="key-shared">
                                            {`Same colour as ${entry.sharedWith.join(', ')}`}
                                        </span>
                                    )}
                                </li>
                            ))}
                        </ul>
                    )}
                </DockSection>
            </div>
        </div>
    );
}
