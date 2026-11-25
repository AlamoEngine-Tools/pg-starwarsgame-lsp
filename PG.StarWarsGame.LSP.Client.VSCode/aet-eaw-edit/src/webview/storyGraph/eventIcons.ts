// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The glyph on a story event type's tile.
//
// The tile's colour says which FAMILY a type is in - the palette groups by it. The glyph says what
// the type itself is, which the colour cannot: sixty-four events share five colours. So each event
// has a glyph of its own, by what happens; two types that are one event under two names share one.
//
// A table and not a rule on the name: the names do not sort that way. STORY_DESTROY_BASE and
// STORY_BASE_DESTROYED are two events, STORY_CORRUPTION_INCREASED is STORY_CORRUPTION_CHANGED.

import type { IconName } from "../shared/Icon";

/** For an event type this build has no glyph for - a schema newer than the extension may name one. */
export const UNKNOWN_EVENT_ICON: IconName = "trigger";

/** Every event type the schema declares, by its technical name in upper case. */
export const EVENT_ICONS: Record<string, IconName> = {
    STORY_ACCUMULATE: "credits",
    STORY_AI_NOTIFICATION: "script",
    STORY_ATTACK_HARDPOINT: "fire",
    STORY_BASE_DESTROYED: "baseLost",
    STORY_BEGIN_ERA: "era",
    STORY_BUY_BLACK_MARKET: "purchase",
    STORY_CAPTURE_HERO: "heroCaptured",
    STORY_CAPTURE_STRUCTURE: "structure",
    STORY_CHECK_DESTROYED: "allDestroyed",
    STORY_CLICK_GUI: "click",
    STORY_COMMAND_UNIT: "command",
    STORY_CONQUER: "planetControl",
    STORY_CONQUER_COUNT: "conquests",
    STORY_CONSTRUCT: "construct",
    STORY_CONSTRUCT_LEVEL: "baseLevel",
    STORY_CORRUPTION_CHANGED: "corruption",
    // The engine registers both names for the one event.
    STORY_CORRUPTION_INCREASED: "corruption",
    STORY_CORRUPTION_TACTICAL_COMPLETE: "missionComplete",
    STORY_CORRUPTION_TACTICAL_FAILED: "missionFailed",
    STORY_DEFEAT_HERO: "heroDefeated",
    STORY_DEPLOY: "deploy",
    STORY_DESTROY: "destroyed",
    STORY_DESTROY_BASE: "baseDestroyed",
    STORY_DIFFICULTY_LEVEL: "difficulty",
    STORY_ELAPSED: "elapsed",
    STORY_ENTER: "orbitEntered",
    STORY_FLAG: "flag",
    STORY_FLEET_BOUNCED: "bounced",
    STORY_FOG_OBJECT_REVEAL: "visible",
    STORY_FOG_POSITION_REVEAL: "positionRevealed",
    STORY_FULL_STOP: "stop",
    STORY_GALACTIC_SABOTAGE: "sabotage",
    STORY_GARRISON_UNIT: "garrison",
    STORY_GENERIC: "genericEvent",
    STORY_GUARD_UNIT: "guard",
    STORY_INVASION_BOUNCED: "invasionBounced",
    STORY_LAND_ON: "landed",
    STORY_LAND_TACTICAL: "landBattle",
    STORY_LOAD_TACTICAL_MAP: "battleStarted",
    STORY_LOSE_BATTLES: "battlesLost",
    STORY_MISSION_FAILED: "missionFailed",
    STORY_MISSION_LOST: "missionLost",
    STORY_MOVE: "heroMoved",
    STORY_NONE: "noEvent",
    STORY_MOVIE_DONE: "animation",
    STORY_OBJECTIVE_TIMEOUT: "objectiveTimeout",
    STORY_OPEN_CORRUPTION: "menuOpened",
    STORY_PLANET_DESTROYED: "planetDestroyed",
    STORY_POLITICAL_CONTROL: "politicalControl",
    STORY_REINFORCE: "reinforced",
    STORY_RETREAT: "retreated",
    STORY_SELECT_PLANET: "planet",
    STORY_SELECT_UNIT: "decision",
    STORY_SPACE_TACTICAL: "spaceBattle",
    STORY_SPEECH_DONE: "speech",
    STORY_TACTICAL_DESTROY: "tactical",
    STORY_TECH_LEVEL: "tech",
    STORY_TRIGGER: "trigger",
    STORY_UNIT_ARRIVED: "arrived",
    STORY_UNIT_PROXIMITY: "proximity",
    STORY_VICTORY: "victory",
    STORY_WIN_BATTLES: "battlesWon",
    STORY_ZOOM_INTO_PLANET: "zoomIn",
    STORY_ZOOM_OUT_PLANET: "zoomOut",
};

/** The glyph of an event type. The engine matches a type name ignoring case, and so does this. */
export function eventIcon(type: string): IconName {
    return EVENT_ICONS[type.toUpperCase()] ?? UNKNOWN_EVENT_ICON;
}
