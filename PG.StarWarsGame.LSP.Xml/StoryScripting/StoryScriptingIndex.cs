// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Xml.StoryScripting;

public static class StoryScriptingIndex
{
    private static readonly Dictionary<string, StoryEventDefinition> _events;
    private static readonly Dictionary<string, StoryRewardDefinition> _rewards;

    static StoryScriptingIndex()
    {
        var events = BuildEvents();
        AllEvents = events.AsReadOnly();
        _events = events.ToDictionary(e => e.EventType, StringComparer.OrdinalIgnoreCase);

        var rewards = BuildRewards();
        AllRewards = rewards.AsReadOnly();
        _rewards = rewards.ToDictionary(r => r.RewardType, StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<StoryEventDefinition> AllEvents { get; }
    public static IReadOnlyList<StoryRewardDefinition> AllRewards { get; }

    public static StoryEventDefinition? GetEvent(string? eventType)
    {
        return eventType is null ? null : _events.GetValueOrDefault(eventType);
    }

    public static StoryRewardDefinition? GetReward(string? rewardType)
    {
        return rewardType is null ? null : _rewards.GetValueOrDefault(rewardType);
    }

    // -----------------------------------------------------------------------
    // Factory helpers
    // -----------------------------------------------------------------------

    private static StoryParamDefinition P(int pos, StoryParamKind kind, bool required,
        string? enumName = null, string? desc = null)
    {
        return new StoryParamDefinition(pos, kind, required, enumName, ReferenceTypeFor(kind), desc);
    }

    private static string? ReferenceTypeFor(StoryParamKind kind)
    {
        return kind switch
        {
            StoryParamKind.PlanetRef => "Planet",
            StoryParamKind.FactionRef => "Faction",
            StoryParamKind.HeroRef => "HeroUnit",
            StoryParamKind.GameObjectTypeRef => "GameObjectType",
            StoryParamKind.GuiElementRef => "CommandBarComponent",
            StoryParamKind.SfxEventRef => "SFXEvent",
            StoryParamKind.SpeechEventRef => "SpeechEvent",
            StoryParamKind.MovieRef => "BinkMovie",
            StoryParamKind.SpecialPowerRef => "SpecialAbility",
            _ => null
        };
    }

    private static StoryEventDefinition Ev(string type, bool supportsFilter,
        params StoryParamDefinition[] p)
    {
        return new StoryEventDefinition(type, supportsFilter, p);
    }

    private static StoryRewardDefinition Rw(string type, params StoryParamDefinition[] p)
    {
        return new StoryRewardDefinition(type, p);
    }

    // -----------------------------------------------------------------------
    // Event definitions  (37 types)
    // -----------------------------------------------------------------------

    private static List<StoryEventDefinition> BuildEvents()
    {
        return
        [
            Ev("STORY_ACCUMULATE", false,
                P(1, StoryParamKind.PositiveInteger, true, desc: "Credit threshold to reach.")),

            Ev("STORY_AI_NOTIFICATION", false,
                P(1, StoryParamKind.FreeString, true, desc: "Lua event name string to fire."),
                P(2, StoryParamKind.PlanetRef, true,
                    desc: "Comma-delimited list of planets on which the trigger can fire.")),

            Ev("STORY_BASE_DESTROYED", true,
                P(1, StoryParamKind.PlanetRef, true, desc: "Planet the base resides on."),
                P(2, StoryParamKind.Enum, true, "StoryBattleMode",
                    "Whether the destroyed base is a space station, ground base, or either.")),

            Ev("STORY_BEGIN_ERA", false,
                P(1, StoryParamKind.EraNumber, true, desc: "Era number (1–5).")),

            Ev("STORY_CHECK_DESTROYED", true,
                P(1, StoryParamKind.FactionRef, true, desc: "Faction whose assets are being checked.")),

            Ev("STORY_CLICK_GUI", false,
                P(1, StoryParamKind.GuiElementRef, true,
                    desc: "Object name of the GUI element from CommandBarComponents.xml.")),

            Ev("STORY_CONQUER", true,
                P(1, StoryParamKind.PlanetRef, true, desc: "Planet whose ownership changes."),
                P(2, StoryParamKind.FreeString, false, desc: "No parameter 2 – leave empty."),
                P(3, StoryParamKind.FactionRef, false, desc: "Faction doing the conquering.")),

            Ev("STORY_CONQUER_COUNT", false,
                P(1, StoryParamKind.PositiveInteger, true, desc: "Number of systems that must be conquered."),
                P(2, StoryParamKind.FactionRef, false, desc: "Faction doing the conquering (filter).")),

            Ev("STORY_CONSTRUCT", true,
                P(1, StoryParamKind.GameObjectTypeRef, true, desc: "Object to watch for construction."),
                P(2, StoryParamKind.PositiveInteger, false, desc: "Number that must be constructed."),
                P(3, StoryParamKind.FactionRef, false, desc: "Side doing the construction (filter).")),

            Ev("STORY_CONSTRUCT_LEVEL", false,
                P(1, StoryParamKind.PlanetRef, true, desc: "Planet on which construction occurs."),
                P(2, StoryParamKind.TechLevel, true, desc: "Station/base level (1–5)."),
                P(3, StoryParamKind.Enum, false, "StoryBattleMode",
                    "Whether the structure is a space station or land base."),
                P(4, StoryParamKind.FactionRef, false, desc: "Side doing the construction.")),

            Ev("STORY_DEFEAT_HERO", false,
                P(1, StoryParamKind.HeroRef, true, desc: "Hero object name (not team object).")),

            Ev("STORY_DEPLOY", false,
                P(1, StoryParamKind.HeroRef, true, desc: "Hero object name (not team object)."),
                P(2, StoryParamKind.PlanetRef, false, desc: "Planet the hero is deployed to.")),

            Ev("STORY_DESTROY", true,
                P(1, StoryParamKind.GameObjectTypeRef, true, desc: "Object type name to track."),
                P(2, StoryParamKind.PlanetRef, false, desc: "Planet where destruction must occur."),
                P(3, StoryParamKind.PositiveInteger, false, desc: "Number of objects that must be destroyed."),
                P(4, StoryParamKind.FactionRef, false, desc: "Who does the destroying.")),

            Ev("STORY_ELAPSED", false,
                P(1, StoryParamKind.FloatSeconds, true,
                    desc: "Seconds to wait. Measured from the prereq trigger, or from campaign start if no prereq.")),

            Ev("STORY_ENTER", true,
                P(1, StoryParamKind.PlanetRef, true, desc: "Planet the unit enters."),
                P(2, StoryParamKind.Enum, false, "StoryEventFilter", "Which faction side triggers the event."),
                P(3, StoryParamKind.HeroRef, false, desc: "Hero that must be present in the entering fleet."),
                P(4, StoryParamKind.HeroRef, false, desc: "Hero that must already be orbiting the planet."),
                P(5, StoryParamKind.FreeString, false, desc: "Unused parameter slot."),
                P(6, StoryParamKind.BooleanInt, false,
                    desc: "1 = bounce the fleet back if param3/4 are not satisfied.")),

            Ev("STORY_FLAG", false,
                P(1, StoryParamKind.FlagNameRef, true, desc: "Name of the flag (created with SET_FLAG)."),
                P(2, StoryParamKind.Integer, true, desc: "Value to compare against."),
                P(3, StoryParamKind.Enum, true, "StoryFlagCompareMethod",
                    "GREATER_THAN | LESS_THAN | EQUAL_TO | GREATER_THAN_EQUAL_TO | LESS_THAN_EQUAL_TO")),

            Ev("STORY_FLEET_BOUNCED", false,
                P(1, StoryParamKind.PlanetRef, true, desc: "Planet that bounced the fleet.")),

            Ev("STORY_GENERIC", false,
                P(1, StoryParamKind.EnumList, true, "StoryGenericTriggerType",
                    "Space-separated list of generic trigger tokens.")),

            Ev("STORY_LAND_ON", true,
                P(1, StoryParamKind.PlanetRef, true, desc: "Planet the unit lands on.")),

            Ev("STORY_LAND_TACTICAL", false,
                P(1, StoryParamKind.StoryPlotFileRef, true, desc: "Land tactical story plot XML filename."),
                P(2, StoryParamKind.PlanetRef, true, desc: "Planet where the land battle triggers the plot.")),

            Ev("STORY_LOAD_TACTICAL_MAP", false,
                P(1, StoryParamKind.PlanetRef, true, desc: "Planet where the battle occurs."),
                P(2, StoryParamKind.HeroRef, false, desc: "Hero that must be in the attacking force."),
                P(3, StoryParamKind.Enum, true, "StoryBattleMode", "Whether this is a ground or space battle.")),

            Ev("STORY_LOSE_BATTLES", false,
                P(1, StoryParamKind.PositiveInteger, true, desc: "Number of battles that must be lost."),
                P(2, StoryParamKind.Enum, false, "StoryBattleMode", "Ground | Space | Either")),

            Ev("STORY_MISSION_FAILED", false,
                P(1, StoryParamKind.StoryPlotFileRef, true,
                    desc: "XML plot file whose loss condition is being watched.")),

            Ev("STORY_MOVE", false,
                P(1, StoryParamKind.HeroRef, true,
                    desc: "Hero to track. Use instead of STORY_ENTER when hero presence is required."),
                P(2, StoryParamKind.PlanetRef, true, desc: "Planet the hero must move to.")),

            Ev("STORY_MOVIE_DONE", false),

            Ev("STORY_SELECT_PLANET", false,
                P(1, StoryParamKind.PlanetRef, true, desc: "Planet the player must select.")),

            Ev("STORY_SELECT_UNIT", false,
                P(1, StoryParamKind.GameObjectTypeRef, true,
                    desc: "Object type name. Any instance of this type will trip the trigger.")),

            Ev("STORY_SPACE_TACTICAL", false,
                P(1, StoryParamKind.StoryPlotFileRef, true, desc: "Space tactical story plot XML filename."),
                P(2, StoryParamKind.PlanetRef, true, desc: "Planet where the space battle triggers the plot.")),

            Ev("STORY_SPEECH_DONE", false,
                P(1, StoryParamKind.SpeechEventRef, true, desc: "Speech event name.")),

            Ev("STORY_TACTICAL_DESTROY", false,
                P(1, StoryParamKind.GameObjectTypeRef, true,
                    desc: "Object type to track. Use STORY_DESTROY for galactic-mode destruction."),
                P(2, StoryParamKind.FreeString, false, desc: "Unused parameter slot."),
                P(3, StoryParamKind.PositiveInteger, true, desc: "Number that must be destroyed.")),

            Ev("STORY_TECH_LEVEL", false,
                P(1, StoryParamKind.TechLevel, true, desc: "Tech level threshold (1–5).")),

            Ev("STORY_TRIGGER", false),

            Ev("STORY_UNIT_PROXIMITY", false,
                P(1, StoryParamKind.GameObjectTypeRef, false,
                    desc: "Unit to track. Leave empty to trigger on ANY object nearing the target."),
                P(2, StoryParamKind.GameObjectTypeRef, true, desc: "Target object (can be an invisible marker)."),
                P(3, StoryParamKind.FloatSeconds, false,
                    desc: "Maximum distance (engine units) before the trigger fires.")),

            Ev("STORY_VICTORY", false,
                P(1, StoryParamKind.FactionRef, true, desc: "Faction that achieves victory.")),

            Ev("STORY_WIN_BATTLES", false,
                P(1, StoryParamKind.PositiveInteger, true, desc: "Number of victories required."),
                P(2, StoryParamKind.Enum, false, "StoryBattleMode", "Space | Land")),

            Ev("STORY_ZOOM_INTO_PLANET", false,
                P(1, StoryParamKind.PlanetRef, true, desc: "Planet being zoomed into.")),

            Ev("STORY_ZOOM_OUT_PLANET", false,
                P(1, StoryParamKind.PlanetRef, true, desc: "Planet being zoomed out from."))
        ];
    }

    // -----------------------------------------------------------------------
    // Reward definitions  (86 types)
    // -----------------------------------------------------------------------

    private static List<StoryRewardDefinition> BuildRewards()
    {
        return
        [
            Rw("ACTIVATE_RETRY_DIALOG"),

            Rw("ADD_OBJECTIVE",
                P(1, StoryParamKind.TextIdRef, true, desc: "Text ID key from the localisation XLS.")),

            Rw("BUILDABLE_UNIT",
                P(1, StoryParamKind.GameObjectTypeRef, true, desc: "Unit type to make buildable."),
                P(2, StoryParamKind.GameObjectTypeRef, false,
                    desc: "Optional unit type to remove from the build menu.")),

            Rw("CHANGE_OWNER",
                P(1, StoryParamKind.GameObjectTypeRef, true, desc: "Object type whose faction is changed."),
                P(2, StoryParamKind.FactionRef, true, desc: "Target faction.")),

            Rw("COMMANDBAR_MOVIE",
                P(1, StoryParamKind.MovieRef, true, desc: "Movie name from MOVIES.XML (no extension needed).")),

            Rw("CREDITS",
                P(1, StoryParamKind.PositiveInteger, true, desc: "Number of credits to add.")),

            Rw("DESTROY_OBJECT",
                P(1, StoryParamKind.GameObjectTypeRef, true, desc: "Object to destroy.")),

            Rw("DISABLE_AUTORESOLVE"),

            Rw("DISABLE_BRANCH",
                P(1, StoryParamKind.BranchNameRef, true, desc: "Branch name to target."),
                P(2, StoryParamKind.FreeString, true,
                    desc: "0/Enable = enable the branch; 1/Disable = disable the branch.")),

            Rw("DISABLE_BUILDABLE",
                P(1, StoryParamKind.GameObjectTypeRef, true, desc: "Object type to remove from the build menu.")),

            Rw("DISABLE_DIRECT_INVASION"),

            Rw("DISABLE_EVENT",
                P(1, StoryParamKind.Enum, true, "StoryTutorialEventType",
                    "TUTORIAL_ZOOM | TUTORIAL_CLICK_GUI | TUTORIAL_ALL"),
                P(2, StoryParamKind.GuiElementRef, false, desc: "Button name for TUTORIAL_CLICK_GUI.")),

            Rw("DISABLE_REINFORCEMENTS",
                P(1, StoryParamKind.BooleanInt, true, desc: "1 = disable, 0 = enable."),
                P(2, StoryParamKind.FactionRef, false, desc: "Faction to affect. Both sides affected if omitted.")),

            Rw("DISABLE_RETREAT",
                P(1, StoryParamKind.FactionRef, true, desc: "Faction whose retreat is toggled."),
                P(2, StoryParamKind.BooleanInt, true, desc: "1 = disable retreat, 0 = enable retreat.")),

            Rw("DISABLE_STORY_EVENT",
                P(1, StoryParamKind.StoryEventNameRef, true, desc: "Event block name to target."),
                P(2, StoryParamKind.BooleanInt, true, desc: "1 = disable, 0 = enable.")),

            Rw("DUAL_FLASH",
                P(1, StoryParamKind.FloatSeconds, true, desc: "Total duration of alternating flashes in seconds."),
                P(2, StoryParamKind.FloatSeconds, true, desc: "Pause between each flash in seconds.")),

            Rw("ENABLE_BUILDABLE",
                P(1, StoryParamKind.GameObjectTypeRef, true, desc: "Object type to restore to the build menu.")),

            Rw("ENABLE_DIRECT_INVASION"),

            Rw("ENABLE_EVENT",
                P(1, StoryParamKind.Enum, true, "StoryTutorialEventType",
                    "TUTORIAL_ZOOM | TUTORIAL_CLICK_GUI | TUTORIAL_ALL")),

            Rw("ENABLE_FOW",
                P(1, StoryParamKind.BooleanInt, true, desc: "1 = enable fog of war, 0 = disable.")),

            Rw("ENABLE_GALACTIC_REVEAL",
                P(1, StoryParamKind.FreeString, true, desc: "Enable | Disable")),

            Rw("ENABLE_OBJECTIVE_DISPLAY",
                P(1, StoryParamKind.BooleanInt, true, desc: "1 = on, 0 = off.")),

            Rw("ENABLE_VICTORY",
                P(1, StoryParamKind.FreeString, true, desc: "Enable (1) | Disable (0)")),

            Rw("FINISHED_TUTORIAL",
                P(1, StoryParamKind.TextIdRef, true, desc: "Tutorial mission text ID.")),

            Rw("FLASH_FLEET_WITH_UNIT",
                P(1, StoryParamKind.GameObjectTypeRef, true, desc: "Object type name (typically a hero).")),

            Rw("FLASH_GUI",
                P(1, StoryParamKind.GuiElementRef, true, desc: "Object name from CommandBarComponents.xml.")),

            Rw("FLASH_PLANET_GUI",
                P(1, StoryParamKind.PlanetRef, true, desc: "Planet to target."),
                P(2, StoryParamKind.Enum, true, "StoryPlanetGuiFlashElement",
                    "FLASH_AFFILIATION | FLASH_FLEET | FLASH_TROOPS | FLASH_PLANET_NAME | FLASH_CREDITS | FLASH_PLANET_VALUE | FLASH_WEATHER"),
                P(3, StoryParamKind.Integer, false, desc: "Fleet index (0–2) when targeting a fleet position."),
                P(4, StoryParamKind.FreeString, false, desc: "Unused."),
                P(5, StoryParamKind.BooleanInt, false,
                    desc: "1 = cursor persists after tactical battles; must be removed by script.")),

            Rw("FLASH_PRODUCTION_CHOICE",
                P(1, StoryParamKind.GameObjectTypeRef, true, desc: "Buildable company/object name.")),

            Rw("FLASH_SPECIAL_ABILITY",
                P(1, StoryParamKind.SpecialPowerRef, true, desc: "Special ability name from the unit's XML entry.")),

            Rw("FLASH_UNIT",
                P(1, StoryParamKind.Enum, true, "StoryCommandBarRegion",
                    "REGION_ORGANIZE | REGION_PRODUCTION | REGION_SELECTION"),
                P(2, StoryParamKind.GameObjectTypeRef, true, desc: "Unit type to flash.")),

            Rw("FORCE_CLICK_GUI",
                P(1, StoryParamKind.GuiElementRef, true, desc: "GUI element from CommandBarComponents.xml.")),

            Rw("FORCE_RESPAWN",
                P(1, StoryParamKind.HeroRef, true, desc: "Hero to respawn.")),

            Rw("FORCE_RETREAT",
                P(1, StoryParamKind.FactionRef, true, desc: "Faction that is forced to retreat.")),

            Rw("HIDE_CURSOR_ON_CLICK",
                P(1, StoryParamKind.BooleanInt, true,
                    desc: "0 = hide last cursor only; 1 = hide all currently displayed cursors.")),

            Rw("HIDE_TUTORIAL_CURSOR"),

            Rw("HIGHLIGHT_OBJECT",
                P(1, StoryParamKind.GameObjectTypeRef, true, desc: "Object to highlight."),
                P(2, StoryParamKind.BooleanInt, true, desc: "1 = highlight, 0 = un-highlight."),
                P(3, StoryParamKind.FreeString, false,
                    desc: "Optional unique ID for the radar blip associated with this object.")),

            Rw("INCREMENT_FLAG",
                P(1, StoryParamKind.FlagNameRef, true, desc: "Name of the flag to increment."),
                P(2, StoryParamKind.Integer, true, desc: "Integer amount to add (positive or negative).")),

            Rw("LINK_TACTICAL",
                P(1, StoryParamKind.PlanetRef, true),
                P(2, StoryParamKind.Enum, true, "StoryBattleMode"),
                P(3, StoryParamKind.FactionRef, true),
                P(4, StoryParamKind.TacticalMapRef, true),
                P(5, StoryParamKind.FactionRef, true),
                P(6, StoryParamKind.GameObjectTypeRef, false),
                P(7, StoryParamKind.StoryPlotFileRef, false),
                P(8, StoryParamKind.BooleanInt, false, desc: "1 = use persistence; 0 = no persistence (default)."),
                P(9, StoryParamKind.BooleanInt, false, desc: "1 = show cinematic (default); 0 = skip."),
                P(10, StoryParamKind.BooleanInt, false, desc: "1 = scene starts faded out."),
                P(11, StoryParamKind.BooleanInt, false, desc: "1 = scene starts in letterbox."),
                P(12, StoryParamKind.FreeString, false),
                P(13, StoryParamKind.BooleanInt, false, desc: "1 = show battle pending dialog; 0 = do not show.")),

            Rw("LOCK_CONTROLS",
                P(1, StoryParamKind.BooleanInt, true, desc: "1 = lock, 0 = unlock.")),

            Rw("LOCK_PLANET_SELECTION",
                P(1, StoryParamKind.BooleanInt, true, desc: "1 = lock, 0 = unlock.")),

            Rw("LOCK_UNIT",
                P(1, StoryParamKind.GameObjectTypeRef, true, desc: "Unit type to lock.")),

            Rw("MOVE_FLEET",
                P(1, StoryParamKind.PlanetRef, true),
                P(2, StoryParamKind.PlanetRef, true),
                P(3, StoryParamKind.Enum, false, "StoryEventFilter")),

            Rw("MULTIMEDIA",
                P(1, StoryParamKind.TextIdRef, false, desc: "Text string or XLS key to display."),
                P(2, StoryParamKind.FloatSeconds, false, desc: "Duration in seconds to display the text."),
                P(3, StoryParamKind.FlagNameRef, false, desc: "Variable to insert into the text."),
                P(4, StoryParamKind.BooleanInt, false, desc: "1 = remove text after duration, 0 = leave."),
                P(5, StoryParamKind.BooleanInt, false, desc: "1 = use teletype display, 0 = normal."),
                P(6, StoryParamKind.FreeString, false, desc: "Text color value (RGB format)."),
                P(7, StoryParamKind.BooleanInt, false, desc: "1 = displayed as subtitle."),
                P(8, StoryParamKind.SpeechEventRef, false, desc: "Speech event ID to play."),
                P(9, StoryParamKind.MovieRef, false, desc: "Command bar movie to play."),
                P(10, StoryParamKind.BooleanInt, false, desc: "1 = loop the movie.")),

            Rw("NEW_POWER_FOR_ALL",
                P(1, StoryParamKind.GameObjectTypeRef, true,
                    desc: "Object type (typically a hero) to receive the power."),
                P(2, StoryParamKind.SpecialPowerRef, true, desc: "Special power name to attach.")),

            Rw("OBJECTIVE_COMPLETE",
                P(1, StoryParamKind.TextIdRef, true)),

            Rw("OBJECTIVE_FAILED",
                P(1, StoryParamKind.TextIdRef, true)),

            Rw("PAUSE_GALACTIC"),

            Rw("PICK_PLANET",
                P(1, StoryParamKind.Enum, true, "StoryEventFilter",
                    "Faction filter – selects from planets owned by the matching side."),
                P(2, StoryParamKind.FlagNameRef, true, desc: "Variable name to assign the selected planet to.")),

            Rw("PLANET_FACTION",
                P(1, StoryParamKind.PlanetRef, true),
                P(2, StoryParamKind.FactionRef, true)),

            Rw("POSITION_CAMERA",
                P(1, StoryParamKind.GameObjectTypeRef, true),
                P(2, StoryParamKind.Vector3, false, desc: "X,Y,Z offset from directly above the object's center.")),

            Rw("REMOVE_ALL_OBJECTIVES"),

            Rw("REMOVE_OBJECTIVE",
                P(1, StoryParamKind.TextIdRef, true)),

            Rw("REMOVE_STORY_GOAL",
                P(1, StoryParamKind.FreeString, true, desc: "Story tag as set in the event block's Story_Tag field.")),

            Rw("REMOVE_UNIT",
                P(1, StoryParamKind.GameObjectTypeRef, true, desc: "All objects with this name will be removed.")),

            Rw("RESET_BRANCH",
                P(1, StoryParamKind.BranchNameRef, true)),

            Rw("RESET_EVENT",
                P(1, StoryParamKind.StoryEventNameRef, true)),

            Rw("RESET_GALACTIC_FILTERS"),

            Rw("REVEAL_ALL_PLANETS"),

            Rw("REVEAL_PLANET",
                P(1, StoryParamKind.PlanetRef, true),
                P(2, StoryParamKind.BooleanInt, true, desc: "0 = hide, 1 = show.")),

            Rw("SCREEN_TEXT",
                P(1, StoryParamKind.TextIdRef, true, desc: "Text name/key from the localisation XLS."),
                P(2, StoryParamKind.FloatSeconds, true,
                    desc: "Seconds to display. Use -1 to keep until explicitly removed."),
                P(3, StoryParamKind.FlagNameRef, false, desc: "Optional text-flag variable to insert."),
                P(4, StoryParamKind.BooleanInt, false, desc: "Set to remove a previously displayed SCREEN_TEXT."),
                P(5, StoryParamKind.BooleanInt, false, desc: "0 = off, 1 = on (default)."),
                P(6, StoryParamKind.FreeString, false, desc: "RGB color value for the text.")),

            Rw("SCROLL_LOCK",
                P(1, StoryParamKind.BooleanInt, true, desc: "1 = disable scrolling, 0 = enable scrolling.")),

            Rw("SELECT_PLANET",
                P(1, StoryParamKind.PlanetRef, true)),

            Rw("SET_FLAG",
                P(1, StoryParamKind.FlagNameRef, true),
                P(2, StoryParamKind.Integer, true)),

            Rw("SET_MAX_TECH_LEVEL",
                P(1, StoryParamKind.FactionRef, true),
                P(2, StoryParamKind.TechLevel, true)),

            Rw("SET_PLANET_RESTRICTED",
                P(1, StoryParamKind.PlanetRef, true),
                P(2, StoryParamKind.BooleanInt, true, desc: "1 = restrict, 0 = unrestrict.")),

            Rw("SET_TACTICAL_MAP",
                P(1, StoryParamKind.TacticalMapRef, true),
                P(2, StoryParamKind.Enum, true, "StoryBattleMode")),

            Rw("SET_TECH_LEVEL",
                P(1, StoryParamKind.FactionRef, true),
                P(2, StoryParamKind.TechLevel, true)),

            Rw("SHOW_COMMAND_BAR",
                P(1, StoryParamKind.BooleanInt, true, desc: "1 = show, 0 = hide.")),

            Rw("SPAWN_HERO",
                P(1, StoryParamKind.HeroRef, true, desc: "Hero TEAM object name (not just the hero unit)."),
                P(2, StoryParamKind.PlanetRef, false,
                    desc: "Planet to spawn on. Defaults to home planet or nearest friendly.")),

            Rw("SPEECH",
                P(1, StoryParamKind.SpeechEventRef, true)),

            Rw("START_CINEMATIC_MODE"),

            Rw("START_MOVIE",
                P(1, StoryParamKind.MovieRef, true)),

            Rw("STOP_CINEMATIC_MODE"),

            Rw("STORY_ELEMENT",
                P(1, StoryParamKind.StoryPlotFileRef, true, desc: "Name of the suspended plot to activate.")),

            Rw("STORY_GOAL_COMPLETED",
                P(1, StoryParamKind.FreeString, true, desc: "Story tag set in the event block's Story_Tag field.")),

            Rw("STORY_OBJECTIVE_TIMEOUT",
                P(1, StoryParamKind.FloatSeconds, true),
                P(2, StoryParamKind.TextIdRef, true)),

            Rw("SWITCH_CONTROL",
                P(1, StoryParamKind.FactionRef, true),
                P(2, StoryParamKind.AiScriptRef, true, desc: "AI script/behavior name to switch to.")),

            Rw("TRIGGER_EVENT",
                P(1, StoryParamKind.StoryEventNameRef, true)),

            Rw("TUTORIAL_DIALOG",
                P(1, StoryParamKind.TextIdRef, true),
                P(2, StoryParamKind.BooleanInt, false, desc: "1 = gray out the continue button.")),

            Rw("TUTORIAL_PLAYER",
                P(1, StoryParamKind.FactionRef, true)),

            Rw("UNIQUE_UNIT",
                P(1, StoryParamKind.GameObjectTypeRef, true,
                    desc: "Use the squadron/company object name, not individual unit names."),
                P(2, StoryParamKind.PlanetRef, false, desc: "System to spawn at.")),

            Rw("UNPAUSE_GALACTIC"),

            Rw("USE_RETRY_DIALOG"),

            Rw("VICTORY",
                P(1, StoryParamKind.FactionRef, true),
                P(2, StoryParamKind.BooleanInt, false,
                    desc: "1 = use reduced delay before returning to galactic mode.")),

            Rw("ZOOM_IN"),

            Rw("ZOOM_OUT")
        ];
    }
}