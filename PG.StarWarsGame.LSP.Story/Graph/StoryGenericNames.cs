// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Story.Graph;

/// <summary>
///     The generic trigger names the game raises, measured from every call site of the engine's
///     generic story event: 36 names from 53 sites, none from Lua (a script's Story_Event call is
///     the AI-notification path). A STORY_GENERIC listener compares its list against the raised
///     name, upper-cased, so a name outside this set is one nothing in the game ever raises - the
///     listener fires only when a TRIGGER_EVENT pushes it. Each name carries what raises it.
/// </summary>
public static class StoryGenericNames
{
    private static readonly Dictionary<string, string> RaisedBy = new(StringComparer.OrdinalIgnoreCase)
    {
        ["battle_end_closed"] = "the battle summary closing",
        ["Continue_Tutorial"] = "the tutorial dialog's Continue button",
        ["close_story_dialog"] = "a story dialog or story summary closing",
        ["end_setup"] = "the setup phase ending",
        ["zoomed_in"] = "the galactic camera zooming in",
        ["zoomed_out"] = "the galactic camera zooming out",
        ["tooltip"] = "a command bar tooltip opening",
        ["fleet_tooltip"] = "the encyclopedia opening on a fleet",
        ["land_tooltip"] = "the encyclopedia opening on land forces",
        ["click"] = "a command bar click",
        ["start_fleet_drag"] = "a fleet drag starting",
        ["start_land_drag"] = "a land force drag starting",
        ["drag_select"] = "a drag selection",
        ["start_hyperspace"] = "a hyperspace move",
        ["drag_unit"] = "a unit dropped on a planet or a fleet",
        ["drag_land_to_space"] = "land forces dropped onto a fleet",
        ["land_to_space"] = "land forces moved to space",
        ["merge_fleets"] = "two fleets merging",
        ["next_planet"] = "the next-planet button",
        ["prev_planet"] = "the previous-planet button",
        ["previous_planet"] = "the previous own or enemy planet button",
        ["ability_slot"] = "a hyperspace special ability's slot",
        ["slice_slot"] = "the slicing slot",
        ["slice"] = "a slice or a tech theft",
        ["slice_dialog"] = "the slicer ability's dialog",
        ["retreat_clicked"] = "the tactical retreat button",
        ["retreat_complete"] = "a retreat completing",
        ["increase_corruption"] = "corruption beginning on a planet",
        ["decrease_corruption"] = "corruption being removed from a planet",
        ["corruption_dialog"] = "the corruption ability's dialog",
        ["close_corruption_dialog"] = "the corruption dialog closing",
        ["choose_corruption_option"] = "a corruption option being chosen",
        ["open_black_market_menu"] = "the black market menu opening",
        ["open_galactic_sabotage_menu"] = "the galactic sabotage menu opening",
        ["start_bombard"] = "a planetary bombardment starting",
        ["super_laser_fired"] = "the super laser firing",
        ["activate_deployed_ability"] = "a deployed hero ability activating",
        ["hero_neutralized"] = "a hero being neutralized"
    };

    /// <summary>Every name the game raises, in the engine's casing.</summary>
    public static IReadOnlyCollection<string> EngineRaised => RaisedBy.Keys;

    /// <summary>Whether the game itself raises this generic name; compared as the engine compares, ignoring case.</summary>
    public static bool IsEngineRaised(string? name)
    {
        return name is not null && RaisedBy.ContainsKey(name.Trim());
    }

    /// <summary>What raises the name in the game, or null for a name the game never raises.</summary>
    public static string? RaiserOf(string? name)
    {
        return name is null ? null : RaisedBy.GetValueOrDefault(name.Trim());
    }
}
