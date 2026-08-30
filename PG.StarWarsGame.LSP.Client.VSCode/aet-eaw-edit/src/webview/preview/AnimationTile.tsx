// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// One ACTION as a full-width card, with the takes it ships.
//
// The card is the action - `Idle`, `Turn left`, `Idle (spread out)` - and pressing it plays the
// action the way the game does: a take drawn at random, and another drawn each time the clip comes
// round again. A flat list of `idle_00 ... idle_08` said none of that; it read as nine idles.
//
// It was a TILE on the shared dock grid, and the user's verdict on that: the idea "was good on
// paper, in reality we have a lot of animations where that works and some where the number of
// alternate indexes escalates so drastically that it is not usable that way". A 128px tile could
// hold about four take chips and the rest went behind a sideways scrollbar, which is exactly the
// case worth seeing. Full width, and the chips WRAP rather than scroll.
//
// The shape, as they set it: the play state on the left, vertically centred across the card; the
// name on the top row, left aligned; the takes on the bottom row, left aligned and filling right.

import { Icon } from '../shared/Icon';
import { type AnimationAction } from './animationNames';

export function AnimationTile(
    { action, playing, onPlay, onPick }: {
        action: AnimationAction;
        /** The clip playing right now, so the card that owns it reads as active. */
        playing: string | null;
        /** Play this action - the caller draws the take. */
        onPlay: (action: AnimationAction) => void;
        /** Play one named take, chosen by hand. */
        onPick: (clip: string) => void;
    },
): React.JSX.Element {
    const mine = action.takes.some(take => take.name === playing);
    const several = action.takes.length > 1;

    return (
        <div className={'anim-card' + (mine ? ' active' : '')}>
            <button
                type="button"
                className="anim-play"
                aria-pressed={mine}
                aria-label={action.label}
                title={several
                    ? `Play ${action.label} - a take at random each time round, as the game does`
                    : `Play ${action.label}`}
                onClick={() => onPlay(action)}
            >
                <Icon name={mine ? 'pause' : 'play'} size={16} />
            </button>

            <span className="anim-card-body">
                <span className="anim-name">{action.label}</span>

                {/* The takes, only where there is a choice. 385 of the 1718 shipped model+action
                    pairs have one; the other 1333 would get a row holding a single chip that says
                    what the name above it already said. */}
                {several && (
                    <span className="anim-takes">
                        {action.takes.map(take => (
                            <button
                                key={take.name}
                                type="button"
                                className={'anim-take'
                                    + (playing === take.name ? ' active' : '')}
                                title={take.name}
                                onClick={() => onPick(take.name)}
                            >
                                {take.label}
                            </button>
                        ))}
                    </span>
                )}
            </span>
        </div>
    );
}
