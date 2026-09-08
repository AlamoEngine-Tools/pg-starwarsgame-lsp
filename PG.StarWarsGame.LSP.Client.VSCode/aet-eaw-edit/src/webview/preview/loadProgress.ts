// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// How far into loading a subject the preview is, and whether the viewport should be covered while
// it happens.
//
// The complaint this answers: a model "renders without textures first, then stuff loads in in
// visible pop ins". That is the load order showing through. Parts are asked for one at a time so a
// capital ship draws its hull without waiting for the last turret, and a part's textures cannot be
// asked for until the geometry that samples them exists - so the honest sequence is: hull appears
// black, turrets appear black one by one, then textures land one by one. Every step of that is
// deliberate and none of it is worth watching.
//
// So the fix is not to reorder the load - it is to stop showing it. The viewport is covered until
// the model is dressed, and what the reader gets instead is a statement of what is being waited on.

/** What has been asked for and what has come back. */
export interface LoadTally {
    /**
     * Parts the scene said would arrive, or null before a scene has been received at all.
     *
     * Null and zero are different: null is "nothing has told us yet", zero is "this scene resolved
     * no parts, so nothing is coming".
     */
    expectedParts: number | null;
    arrivedParts: number;
    /** Distinct assets asked for, and how many have been ANSWERED - found or not. */
    requestedAssets: number;
    settledAssets: number;
    /** The wait was abandoned rather than finished. */
    timedOut: boolean;
}

export type LoadStage = 'scene' | 'geometry' | 'materials' | 'ready';

export interface LoadState {
    stage: LoadStage;
    /** Whether the half-built model should be hidden behind the cover. */
    covered: boolean;
    label: string;
    /** How far through this stage is, as the reader would count it, or null when nothing to count. */
    detail: string | null;
}

/**
 * Reads the tally.
 *
 * Counts rather than a percentage, deliberately. The asset total GROWS as parts land and again when
 * particle systems attach, so a bar would visibly run backwards; "12 of 30" becoming "12 of 34" is
 * the same fact stated in a way that is not a lie.
 */
export function loadState(tally: LoadTally): LoadState {
    const ready: LoadState = {
        stage: 'ready', covered: false, label: 'Ready', detail: null,
    };

    if (tally.timedOut) {
        return ready;
    }

    if (tally.expectedParts === null) {
        return { stage: 'scene', covered: true, label: 'Reading the scene', detail: null };
    }

    // A scene that resolved no parts sends no GLB request, so no part will ever arrive. Waiting on
    // one here would leave the cover down for the rest of the session, over a viewport that has
    // nothing to hide.
    if (tally.expectedParts === 0) {
        return ready;
    }

    if (tally.arrivedParts < tally.expectedParts) {
        return {
            stage: 'geometry',
            covered: true,
            label: 'Loading geometry',
            detail: `${tally.arrivedParts} of ${tally.expectedParts}`,
        };
    }

    if (tally.settledAssets < tally.requestedAssets) {
        return {
            stage: 'materials',
            covered: true,
            label: 'Loading textures',
            detail: `${tally.settledAssets} of ${tally.requestedAssets}`,
        };
    }

    return ready;
}

/**
 * The assets asked for, and the ones answered.
 *
 * Its own object because the counting has a trap in it. The host answers each name ONCE - it keys
 * `requestedTextures` by the lower-cased name and returns early on a repeat, without replying - and
 * every part that loads reports the whole scene's texture list. So a ten-part unit posts its shared
 * hull texture ten times and gets one reply. Counting the posts would leave nine replies
 * outstanding forever, and the cover would never lift.
 *
 * The rules here mirror `ModelPreviewPanel.sendTexture` exactly, including the empty name it drops
 * in silence. If that method's keying ever changes, this has to change with it.
 */
export class AssetLedger {
    private readonly asked = new Set<string>();
    private readonly answered = new Set<string>();

    /** Distinct names asked for. */
    get requested(): number {
        return this.asked.size;
    }

    /** How many of them have come back. */
    get settled(): number {
        return this.answered.size;
    }

    /**
     * Records a request.
     *
     * @returns Whether this is a name worth posting - false for a repeat or an empty name, both of
     *     which the host would answer with nothing.
     */
    request(name: string): boolean {
        const key = name.toLowerCase();

        if (key === '' || this.asked.has(key)) {
            return false;
        }

        this.asked.add(key);
        return true;
    }

    /**
     * Records an answer.
     *
     * A texture that did not resolve counts the same as one that did: the host replies either way,
     * and the wait is over. Nothing here tells them apart - the missing one is reported by the
     * problems bar, which is where a reader can act on it.
     */
    settle(name: string): void {
        const key = name.toLowerCase();

        // Only what was actually waited on. An answer to something never asked for would push the
        // settled count past the requested one and read as done while parts were still missing.
        if (this.asked.has(key)) {
            this.answered.add(key);
        }
    }

    clear(): void {
        this.asked.clear();
        this.answered.clear();
    }
}
