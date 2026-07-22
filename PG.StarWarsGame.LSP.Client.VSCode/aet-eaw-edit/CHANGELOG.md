# Changelog

## 0.3.0

### Features

- XML tags close themselves as you type: typing the `>` that ends an opening tag now inserts the matching `</Tag>` and leaves the cursor between the two, so hand-typed tags behave the way accepting a tag-name completion already did. Self-closing tags, closing tags, comments/processing instructions, and elements that already have a closing tag are left alone; the inserted name preserves the source casing. Opt-out via `aet-eaw-edit.features.xml.autoCloseTag`. Because it rides on VS Code's on-type formatting it only fires when `editor.formatOnType` is enabled, which is off by default - the same way the existing linked editing of tag pairs (rename an opening tag and its closing tag follows) needs `editor.linkedEditing`. Both settings are cross-linked from the extension's own settings so the editor toggle is a click away.

- Variant inheritance is readable at a glance: a tag that changes an inherited value is marked inline with what it displaced (`overrides 99`, `adds to 3 inherited`), and the *Show effective object* view now names the replaced value next to each overridden tag instead of only saying it was overridden. Additive tags - ones the engine accumulates rather than replaces, like `Death_Clone` - are called out where they are set, explaining that the base's entries are kept as well, so a re-skinned hero variant that quietly inherits the base model's damage clones is visible before it ships. See [#73](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/73) and [#63](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/63).

- Galactic ability lists resolve: the ability names in `GameConstants` (`Activated_Sabotage_Ability_Names` and its nine siblings) and `BlackMarketItem.Ability_Names` are now real references. Ctrl+Click jumps to the ability, and a name no unit defines is flagged. These name an ability without saying which object owns it - which the engine accepts - so they are matched across owners rather than against the owning unit.

- `Campaign.Autoresolve_Exclusion_Locations` is understood as the (planet, mode) pair list it is: the planets are references you can navigate and validate, the modes are checked against the known battle modes, and a planet left without a mode is reported - a mistake that silently throws every following pair out of step.

- Hardpoint bone validation: a hardpoint declared `Is_Destroyable` but with no `Attachment_Bone` is reported as an error - the engine has nothing to attach it to, so it becomes **indestructible** and the unit keeps a weak point that can never be shot off. Beyond that, the bones a hardpoint names are cross-checked against the models of every game object that mounts it: `Attachment_Bone`, `Collision_Mesh`, `Damage_Decal` and `Damage_Particles` against the mounting unit's models (all of them, resolved through variant inheritance), and `Turret_Bone_Name`/`Barrel_Bone_Name` against the hardpoint's own `Model_To_Attach` - with `Fire_Bone_A`/`_B` following whichever side `Is_Turret` puts them on. Checks run from both ends, so the problem is visible whether you have the hardpoint file or the unit file open. Where a model's bones could not be read at all the extension says so rather than staying silent, so a missing or unreadable `.alo` is never mistaken for a clean bill of health. See [#53](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/53).

- Reference-list tags that use `|` (OR) where the engine only understands it as AND are now flagged. In a plain list tag (`GameObjectTypeReferenceList`, `TypeReferenceList`, `NameReferenceList` and per-faction object lists) a `|` looks like an OR but the engine's reference splitter silently treats it as just another separator, so every listed value is required after all. The error explains this and offers a quick fix that rewrites the value with commas; tags actually documented for OR-expressions (like `Required_Special_Structures`) are left alone. See [#64](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/64).

- Story-dialog navigation & hints: dialog `.txt` scripts gain inlay hints (opt-in via `aet-eaw-edit.features.dialog.inlayHints`) showing the referenced localisation text at the end of `TEXT`/`TITLE` lines - or a MISSING marker for unknown keys - and go-to-definition (opt-in via `aet-eaw-edit.features.dialog.goToDefinition`) jumping from `DIALOG`, `MOVIE`/`MOVIE_ONCE`, and `SFX` arguments to the defining XML object. Both apply only to files under the `.pgproj` `directories.storyDialog` folders.

- Story navigator & read-only graph viewer (opt-in via `aet-eaw-edit.features.tools.storyEditor` + `aet-eaw-edit.features.story.discovery`): a new *EaWEdit: Story* activity-bar view lists every campaign → faction → plot threads and attached Lua scripts (suspended threads marked; clicking opens the file). The graph icon on a campaign - or the *EaWEdit: Open Story Graph* command - opens a read-only graph panel: auto-laid-out event flow with AND/OR junctions, cross-file portals and tactical plots, node colours by lifecycle (inactive/waiting/armed/fired/disabled), dimmed unreachable events, and dashed borders on schema-untested event/reward types. A toolbar filters by name, branch and lifecycle; selecting an event shows its full property view with *Open XML* (jumps to the event block) and *Reachable from here* (trims the graph to everything downstream). The view refreshes live as story files are edited. See [#87](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/87).

- Cross-language story rename (opt-in via `aet-eaw-edit.features.story.rename`, builds on story symbols): renaming a story event, flag, or AI-notification id - from XML or from Lua - updates the definition and every reference across story threads and scripts in one workspace edit. Guard rails match engine semantics: an event name defined more than once in the workspace is rejected instead of mass-renamed (disambiguate first), and story flag names are capped at the engine's 31-character limit. See [#85](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/85).

- Cross-language story symbols (opt-in via `aet-eaw-edit.features.story.symbols`, builds on story discovery): story event names, flags, and AI-notification ids are indexed across XML and Lua. Go-to-definition works from a `Prereq` token or `TRIGGER_EVENT` parameter to the event block, and from a `STORY_AI_NOTIFICATION` id straight to the Lua `Story_Event("…")` call that fires it; `StoryModeEvents` table keys and `Check_Story_Flag` arguments are linked back too. Story event/reward parameters whose schema names a real object type (planets, units, factions, speech events) are full references too: Ctrl+Click jumps to the defining XML, hover works, and unknown values are flagged by the same validation the rest of the workspace uses - including the engine-placeholder exemption (`None`/`null`/`Default` are no longer false positives). See [#84](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/84).

- Story campaign graph diagnostics (opt-in via `aet-eaw-edit.features.story.graphDiagnostics`, builds on story discovery): story thread files are analysed as part of their whole campaign - dangling or cyclic prerequisites, duplicate event names in one file, ambiguous campaign-global event targets (`TRIGGER_EVENT` resolves campaign-wide), events that can never fire, suspended plots that nothing activates, deviations from the documented event tag order, and flag names over the engine's 31-character limit. See [#83](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/83).

- Campaign story-chain discovery (opt-in via `aet-eaw-edit.features.story.discovery`): campaigns, story plot manifests, and story thread files are followed from `CampaignFiles.xml` and typed, activating story event/reward parameter validation and completion in story files. Broken links in the chain (a `*_Story_Name` or plot entry pointing at a missing file, tactical plot references, malformed manifests) are reported as diagnostics on the referencing line. See [#82](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/82).

- Story-dialog language service (opt-in via `aet-eaw-edit.features.dialog.diagnostics`): dialog `.txt` scripts get diagnostics - unknown commands, wrong argument counts and types, warnings for documented-but-untested commands, and reference checks for localisation keys (`TEXT`/`TITLE`), speech events (`DIALOG`), movies (`MOVIE`/`MOVIE_ONCE`) and sound events (`SFX`). Which `.txt` files are dialog scripts is declared in the `.pgproj` via the new `directories.storyDialog` node - filename conventions play no part. Story events cross-check too: a `Story_Dialog` that doesn't resolve inside the declared scope and a `Story_Chapter` pointing at a chapter the script doesn't define are flagged. See [#89](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/89).

### Bug fixes

- Go-to-definition works on a class of tags where it previously did nothing at all - silently, with no diagnostic either, because the values were never indexed as references. Fixed across 36 tags, including the skirmish AI force lists (`Space_Skirmish_AI_Default_Forces`, `Land_Skirmish_AI_Default_Forces`), faction `Allies`/`Enemies`, `Preferred_Pathfinder_Types`, the random-story unit lists, and tags that reference bones, icons, maps and localisation keys. The same values are now validated too, so a typo is reported rather than ignored. See [#77](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/77).

- Go-to-definition works on the SFX half of tuple-valued sound tags (`SFXEvent_Hardpoint_Destroyed`, `SFXEvent_Attack_Hardpoint`, the GUI ability toggles and friends). The event name resolved fine from a plain SFX tag but did nothing inside these pairs, which looked like SFX navigation being unreliable. See [#78](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/78).

- Inlay hints and code lenses no longer go stale after a localisation change. Editing a translation, adding a language, or a loca file changing on disk refreshed the data but never told the editor, so localisation-backed annotations kept showing the old text until you opened another file. See [#45](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/45).

- Additive tags no longer lose data in the *Show effective object* view. A repeatable additive tag such as `Death_Clone` was flattened into a single comma-separated value with duplicate tokens dropped, which detached the surviving clone names from their damage types and produced XML that could not be pasted back. Each entry now survives as its own element, base entries first.

- Multiple `<Prereq>` lines on one story event are no longer reported as duplicate tags. They are an OR of AND-groups, and every OR-chained event drew a spurious warning. See [#60](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/60).

- Localisation editor icons (the search-mode toggles and the reset-to-inherited gutter arrows) now appear in the packaged extension. They were loaded from `node_modules`, which is not part of the published VSIX, so they only showed up when running the extension from source.

- Hovering over an XML comment no longer writes a spurious warning to the server log. The "no hover found" case on comments was logged at warning level; it is now debug, so a clean session stays quiet. See [#72](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/72).

## 0.2.0

### Breaking changes

- **`.pgproj` localisation configuration moved out of `directories`.** The `directories.text` array and `directories.textResourceType` string are removed. Localisation is now declared in a new top-level `localisation` node:

  ```jsonc
  // Before (0.1.x)
  "directories": {
    "xml": ["data/xml"],
    "text": ["data/text"],
    "textResourceType": "Csv"
  }

  // After (0.2.0)
  "directories": {
    "xml": ["data/xml"]
  },
  "localisation": {
    "type": "CSV",
    "directory": "data/text"
  }
  ```

  `type` is one of `CSV`, `DAT`, `XML`, `NLS` (uppercase). **A `.pgproj` left in the old shape now fails to load, with a notification explaining the fix** - the server refuses to guess, rather than silently indexing your mod without localisation. If you have an existing `.pgproj`, edit it before or right after upgrading. See [Upgrading from 0.1.x](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/blob/master/PG.StarWarsGame.LSP.Client.VSCode/aet-eaw-edit/README.md#upgrading-from-01x) in the README for the full migration steps, including clearing cached indexes.
- **Multiple `.pgproj` files under one workspace root now fail startup with a notification** instead of silently picking one at random. If you have more than one `.pgproj` under the folder you open in VS Code (for example, a leftover backup copy), remove or relocate the extras, or open the specific subfolder that contains the one you want to use.

### Features

- Feature flags: independently enable or disable XML, Lua, and cross-language tooling capabilities via new `aet-eaw-edit.features.*` settings. Changing any flag automatically restarts the language server. Lua hover, Lua diagnostics, and the localisation tooling (editor panel, initialise/import commands, create-key code action) ship disabled by default while still in development - enable the corresponding setting to opt in early.
- Text Editor overhaul: clearer, more consistent editing experience. See [#55](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/55).
- Import existing localisation projects into a `.pgproj`. See [#56](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/56).
- `.pgproj` localisation support extended to all supported formats. See [#57](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/57).
- Support for `.pgproj` localisation merge chains. See [#58](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/58).

### Lua / EmmyLua Support

- Layer-ranked `require()` resolution. See [#3](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/3).
- Relative `require()` support. See [#4](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/4).
- Cross-mod tier classification regression test suite. See [#5](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/5).
- Doc comment extraction into hover documentation. See [#6](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/6).
- Workspace Lua function hover documentation. See [#7](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/7).
- EmmyLua annotation parser and data model. See [#8](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/8).
- Workspace type registry (`LuaTypeIndex`). See [#9](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/9).
- `.d.lua` declaration file indexing. See [#10](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/10).
- Member access completion from `LuaTypeIndex`. See [#11](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/11).
- Type hover from `LuaTypeIndex`. See [#12](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/12).
- `LuaApiSchemaProvider` extended to parse `@class` and `@field` annotations. See [#13](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/13).

### Bug Fixes

- Fixed `Land_Terrain_Model_Mapping` incorrect format causing an error. See [#23](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/23).
- Fixed `Presence_Induced_Animations` incorrect format causing an error. See [#24](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/24).
- Stopped flagging valid behaviours. See [#25](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/25).
- Fixed `SurfaceFX_Name` content issue. See [#27](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/27).
- Stopped flagging valid 64-bit category masks. See [#28](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/28).
- Stopped flagging valid ability names. See [#30](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/30).
- Fixed `Hardpoint::Damage_Particles` being incorrectly flagged. See [#38](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/38).
- Fixed Min/Max Pitch issue. See [#40](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/40).
- Fixed `MSS_3D_Provider_Name` duplicate tag flagging. See [#41](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/41).
- Fixed multiple "Defaults" issue. See [#42](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/42).
- Fixed `Factions.xml` flagged musicevents. See [#43](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/43).
- Fixed `Land_Skirmish_Unit_Cap_By_Player_Count` issue. See [#44](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/44).
- Fixed invalid-but-valid `Damage_To_Armor_Mod` flagging. See [#47](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/47).
- Stopped flagging tags that are fine and necessary to duplicate. See [#48](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/48).
- Fixed base game `Damage_Type` issue. See [#49](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/49).
- `TALK` is now recognized as a valid animation. See [#50](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/50).
- Fixed `Map_Load_Spawn_Table` spawn probability issue. See [#51](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/51).

## 0.1.2

- Hotfix for URI encoding/decoding issues causing cash misses. See [#20](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/20).

## 0.1.1

First public preview release.

- XML completions, hover, diagnostics, go-to-definition, find-all-references, rename, code actions, code lens
- Variant inheritance: Show Effective Object command resolves `Variant_Of_Existing_Type` chains
- Lua script indexing and diagnostics
- Localisation editor panel (CSV, XML, DAT, Properties formats)
- Mod project file (`.pgproj`) support with multi-project references
- Server version check on startup with download prompt if version does not match
