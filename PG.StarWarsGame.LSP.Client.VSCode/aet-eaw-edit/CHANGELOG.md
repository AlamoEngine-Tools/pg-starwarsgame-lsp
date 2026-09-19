# Changelog

## Unreleased

### Features

- **Lua debugger** - off by default, flag `aet-eaw-edit.features.lua.debugger`. See [#142](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/142).
  - New debug type **Empire at War Lua** for the game's debug build
  - Breakpoints in project scripts; attach to a running game, or launch it with the mod chain
  - Call stack, locals, watches, hovers, and a Lua Debug Console
  - **Lua Scripts** view in Run and Debug: every running script instance with its coroutine threads; break in a script or a thread
  - Adapter: the language server binary in a second mode, nothing extra to install
  - Settings under `aet-eaw-edit.game.*`: executable, arguments, host, port, table expansion
  - Limits inherited from the game, reported as such:
    - Breakpoint conditions never evaluated
    - Whole game frozen while a script is stopped
    - Pause at the next Lua line
    - Locals from the source parse
    - Table expansion only with the unsafe switch
  - Launch refuses a layer not laid out under `Data/` or with a space in its path; a build step for such projects is planned ([#144](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/144))

- **Model preview** - flag `aet-eaw-edit.features.tools.modelPreview`, on by default. See [#137](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/137).
  - **Alamo Model Preview** editor for `.alo` and `.ala` files
  - **Preview Assembled Unit** (command and code lens): a GameObject as the game builds it - hardpoints on their attachment bones, movable turrets, firing arcs, team colour
  - *Model* lens: model tree, LOD and ALT levels, skeleton, cameras, inspector, scene and light settings
  - *Animation* lens: clip library by family, action and take; the game's loop rule; transport; playback speed
  - *Gameplay* lens: attacker weapons and abilities, shields, hardpoint destroy and repair with damage smoke and death clone, damage log naming the armour type per hit
  - Particle systems: effects, groups, emitters
  - Saved shots
  - Handoff to AloViewer and the Particle Editor via `aet-eaw-edit.tools.*`
  - **Set Up Base Shader Sources**: Petroglyph's published shader sources into `aet-eaw-edit.shaders.directory`; never redistributed
  - Game models need `aet-eaw-edit.lsp.source.baseGameDirectory` and `expansionDirectory`
  - Energy pool behind `aet-eaw-edit.features.preview.energyPool`, off on purpose
  - Not covered: sounds, animation SFX maps

- **Encyclopedia popup preview** - flag `aet-eaw-edit.features.tools.encyclopedia`, on by default. Read-only.
  - **Preview Encyclopedia Popup** (command and code lens): the in-game tooltip card
  - Icon, name, class line, `Encyclopedia_Text` wrapped as the game wraps it, ship names, faction switch
  - Game language from `aet-eaw-edit.lsp.localisation.language`

- **Mega-texture icons**
  - Game icons baked into the baseline
  - A project's own `mt_commandbar` replaces them wholesale, as in the engine
  - `.pgproj` `icons` node: `megaTexture` (atlas pair, no extension), `sourceRoots` (loose files)
  - Diagnostics: icon missing everywhere = error; in sources but not in the mega texture = warning to rebuild

- **About 45 new XML checks from the engine's own rules.** Schema audited tag by tag against the game's parser over 24 sweeps; value handlers 105 to 150, cross-tag rules 2 to 14.
  - Unknown or misspelled tags, with a suggestion
  - Unnamed objects
  - Missing required tags
  - Numeric ranges: angles, percentages, counts, durations
  - Tags required or forbidden together
  - Tag comparisons: damage vs chase radius, respawn min vs max
  - `Land_Damage_*` trio lengths ([#102](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/102))
  - Capture clone without eject ([#103](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/103))
  - Death animations on the model ([#104](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/104))
  - Absorb settings that heal nothing ([#106](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/106))
  - Standalone space-map special weapons ([#98](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/98), [#99](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/99))
  - Attack distance beyond hardpoint range ([#101](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/101))
  - Variant chains too deep, cyclic, or with a missing base
  - Case-sensitive tags; engine text limits
  - Missing or misnamed tactical maps are errors ([#132](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/132))
  - `Excluded_Unit_Categories` accepts a list ([#123](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/123))
  - Model names with spaces resolve ([#124](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/124))
  - `Debug_Hot_Key_Load_Map_Script` is a plot-file reference ([#109](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/109))
  - No `Tactical_Health` rule: the shipped game matches it on no unit and the engine never compares the totals ([#100](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/100)). Instead, three hardpoint faults that make a unit unkillable:
    - No attachment bone
    - Missing or duplicated collision mesh
    - Collision mesh on no model
  - A reference that cannot be checked (no baseline, no game directory) is reported as such under its own id, not as broken

- **Story graph fixes**
  - Colour key flyout for node, border and edge styles, including per-branch hues ([#128](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/128))
  - Reachability filter isolates an event's chain, incoming or outgoing ([#126](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/126))
  - Lightweight overview and windowed nodes keep campaigns of 1000+ events responsive ([#131](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/131))
  - Branch filter facets ([#130](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/130))
  - Problems bar follows the branch filter; jumping to a hidden problem lifts it ([#129](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/129))
  - Nodes and boxes aligned ([#127](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/127))
  - Optional story params no longer demanded ([#125](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/125))
  - `STORY_ELAPSED` accepts `Event_Param2` ([#134](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/134))

- **Breaking: settings moved.** Read by the preview, the encyclopedia card and the asset checks, not ModVerify alone.
  - `aet-eaw-edit.modVerify.baseGameDirectory` -> `aet-eaw-edit.lsp.source.baseGameDirectory`
  - `aet-eaw-edit.modVerify.expansionDirectory` -> `aet-eaw-edit.lsp.source.expansionDirectory`

- **Localisation commands**
  - **Set Localisation Project Format** writes `localisation.type` ([#121](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/121))
  - **Open Localisation File as Text**
  - **Refresh Localisation Files**
  - **Convert Localisation File to Another Format**
  - **Export Localisation to DAT**

- **Dependencies updated.** React 19; current language client, VS Code typings, ESLint and build tooling; current server patch releases. Held back:
  - `elkjs` - the auto-arrange plugin supports 0.8.x only
  - `typescript` - the ESLint plugin does not accept 7.x
  - `@vscode/codicons` - the latest version is a prerelease

- **Diagnostic suppression in every language.** See [#66](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/66), [#67](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/67), [#69](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/69).
  - Directives: `<!-- aetswg:suppress ... -->` (XML), `-- aetswg:suppress ...` (Lua), `# aetswg:suppress ...` (dialog)
  - Scopes: next item; enclosing declaration (`-object`); file (`-file`); project (`.aetswg/suppressions.json`, meant to be committed)
  - Comma lists, `reason::` notes, group wildcards (`aetswg-004-*`)
  - Quick fixes write the directive, narrowest scope first; dialog quick fixes behind `aet-eaw-edit.features.dialog.codeActions`
  - Project-wide suppression refreshes XML, Lua and dialog at once
  - Unreadable directives reported on the comment: `aetswg-013-0001` (bad entry), `aetswg-013-0002` (no id); good entries in the same list still apply

- **Stable diagnostic ids** `aetswg-<group>-<number>`, shown next to every message. See [#68](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/68).
  - Groups classify the problem, not the language
  - One id per distinct problem
  - Lua parse errors keep Loretta's number: `LUA1003` = `aetswg-012-1003`

- **Breaking:** `<!-- lsp:suppress duplicate-symbol -->` is no longer honoured; replace with `<!-- aetswg:suppress aetswg-010-0001 -->`. `<!-- <Override Name="..."/> -->` is unchanged: a declaration of intended shadowing, not a suppression; shadow warnings now say "declare it".

- **Localisation editor in editor tabs**
  - **Localisation Editor** activity bar view; **Text files** and **Credits files**; one grid tab per file, splittable side by side
  - Staged edits, explicit **Save** with a count; one all-or-nothing write; refused if the file changed on disk
  - **Validate** without writing, on open and on demand; results in a bar under the table
  - Saves rewrite changed rows only; quoting, comments and line endings preserved
  - Row context menu per file kind; **Add translation...** dialog with key suggestions from lower layers
  - Inherited rows hidden by default; **Inherited** toggle; **Reset to inherited value**
  - Column sort on text files
  - Language columns from a header gear; empty languages hidden at first
  - Footer with row and filter counts
  - Filter by text, wildcard or regex with scope; applied after typing stops; invalid patterns flagged
  - **Add language** and **Fill language** (from another language, or from the game) on the dock
  - Conversion to another format keeps the original
  - Compiled `.dat` files editable for `"type": "DAT"` projects
  - `.pgproj` format matched case-insensitively
  - Initialise and import report every outcome
  - Removed: `aet-eaw-edit.localisation.editorEnabled`

- **Credits files as their own editor**
  - Ordered, position-addressed rows: insert above and below, move, delete
  - Blank spacer rows (`[TBL]`) shown as rules
  - Tile library of the three line kinds (label, name, blank) with drag and drop
  - Directives `HEADER` and `CENTER` from a dropdown
  - Detection by engine naming (`credits*`) or `localisation.credits` in the `.pgproj`: `detection` (`convention`, `explicit`, `none`) and `files`
  - Export writes `CreditsText_LANGUAGE.dat` in file order
  - Preview crawl (**Open Credits Preview to the Side**, **Play Credits Crawl Full Screen**): staged rows as the game plays them, generated starfield, fixed reading pace, reduced-motion fallback
  - Validate reports empty headings, not per-language gaps

- **Shared chrome**
  - Story graph and localisation docks: one tile grid, one section style, one dock width
  - Codicons replace typed glyphs
  - Filter controls share the graph editor's styling
  - Fixed: validation results placement, row menus off screen, sorted headers in light themes, stray horizontal scrollbar
  - Localisation and Story views show "Loading..." before the scan completes and preload when it finishes

### Improvements

- **Typed project files.** `.pgproj`, `.aetswg/story-layout.json`, `.aetswg/suppressions.json` and workspace settings carry `_type` and `_typeVersion` and are migrated on format changes.
  - Sidecars migrate in place
  - A `.pgproj` migration is offered once per project per session, with a note on what changed

- **Schema version check.** The schema manifest declares `schemaVersion`.
  - Major version too new: refused with an update message
  - Older schemas: load as before
  - A tag whose value shape the extension cannot interpret: validation withheld, not guessed

### Bug fixes

- **Re-validate Workspace** covers XML, Lua and dialog; one language failing no longer stops the rest.

- **Campaign story attachments** validated as you type.
  - A `Faction, PlotFile` tuple in a faction-specific tag: error with a quick fix
  - A faction attached twice: warning (same manifest) or error (different manifests); paths compared in normal form
  - The faction half of a `<Story_Name>` tuple is a real reference: navigation, rename, unknown-faction check
  - Broken story-chain links (`*_Story_Name`, `Active_Plot`, `Suspended_Plot`, tactical plots) come from the live chain and update on every edit

- **Metafile override rule.** A registry (`GameObjectFiles.xml`, `CampaignFiles.xml`, ...) shipped by several layers resolves to the highest-ranked copy only, as in the engine. Entries a mod's copy omits are no longer indexed from a dependency's; expect previously masked unknown-reference reports.

## 0.3.1

### Improvements

- **Story graph on very large campaigns.** A campaign with over a thousand events opens near-instantly and stays responsive.
  - Zoomed out: a lightweight overview of branch-coloured tiles, titles fading in with zoom
  - Zoomed in: real, interactive nodes for the visible area only
  - Minimap, swimlanes, Fit, Arrange and jump-to-problem work against the overview
  - Edit mode no longer renders the whole graph for a change

### Bug fixes

- **Generic `Story_Name` attachments** are discovered, navigated and validated.
  - The flat `Faction, PlotFile[, Faction, PlotFile ...]` tuple form (used by EaWX, and the only form for non-major factions)
  - Merged with `Rebel_Story_Name`, `Empire_Story_Name` and `Underworld_Story_Name` into one (faction, plot) list, as in the engine

## 0.3.0

### Features

- **Auto-close XML tags.** Typing the `>` of an opening tag inserts `</Tag>` and places the cursor between the two.
  - Left alone: self-closing tags, closing tags, comments, processing instructions, elements already closed
  - Source casing preserved
  - Opt-out: `aet-eaw-edit.features.xml.autoCloseTag`
  - Needs `editor.formatOnType`; linked editing of tag pairs needs `editor.linkedEditing`; both cross-linked from the extension's settings

- **Variant inheritance at a glance.** See [#73](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/73), [#63](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/63).
  - Inline marks on overriding tags: `overrides 99`, `adds to 3 inherited`
  - *Show effective object* names the replaced value next to each overridden tag
  - Additive tags (accumulated by the engine, e.g. `Death_Clone`) called out where set

- **Galactic ability lists resolve.** `GameConstants` ability names (`Activated_Sabotage_Ability_Names` and its nine siblings) and `BlackMarketItem.Ability_Names` are references: Ctrl+Click navigates, unknown names are flagged. Matched across owners, as the engine accepts them.

- **`Campaign.Autoresolve_Exclusion_Locations`** understood as a (planet, mode) pair list.
  - Planets navigable and validated
  - Modes checked against the known battle modes
  - A planet without a mode reported (it throws every following pair out of step)

- **Hardpoint bone validation.** See [#53](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/53).
  - `Is_Destroyable` without `Attachment_Bone`: error - the hardpoint becomes indestructible
  - `Attachment_Bone`, `Collision_Mesh`, `Damage_Decal`, `Damage_Particles` checked against every mounting unit's models, through variant inheritance
  - `Turret_Bone_Name`, `Barrel_Bone_Name` checked against the hardpoint's `Model_To_Attach`; `Fire_Bone_A`/`_B` follow `Is_Turret`
  - Checked from both the hardpoint file and the unit file
  - Unreadable model bones reported, never silently passed

- **`|` in plain reference lists** flagged: the engine treats it as another separator (AND), not OR. Quick fix rewrites with commas; tags documented for OR-expressions (e.g. `Required_Special_Structures`) exempt. See [#64](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/64).

- **Story-dialog navigation and hints** for `.txt` scripts under `directories.storyDialog`.
  - Inlay hints (`aet-eaw-edit.features.dialog.inlayHints`): localisation text after `TEXT`/`TITLE` lines, or a MISSING marker
  - Go to definition (`aet-eaw-edit.features.dialog.goToDefinition`): from `DIALOG`, `MOVIE`/`MOVIE_ONCE` and `SFX` arguments to the XML object

- **Story navigator and read-only graph viewer** (`aet-eaw-edit.features.tools.storyEditor` + `aet-eaw-edit.features.story.discovery`). See [#87](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/87).
  - *EaWEdit: Story* view: campaign, faction, plot threads, attached Lua scripts; suspended threads marked
  - Graph panel (*EaWEdit: Open Story Graph*): auto-laid-out event flow, AND/OR junctions, cross-file portals, tactical plots
  - Node colours by lifecycle; unreachable events dimmed; schema-untested types dashed
  - Toolbar filters: name, branch, lifecycle
  - Event property view with *Open XML* and *Reachable from here*
  - Live refresh on edits

- **Cross-language story rename** (`aet-eaw-edit.features.story.rename`). See [#85](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/85).
  - Events, flags and AI-notification ids renamed across threads and scripts in one workspace edit
  - Refused for an event name defined more than once
  - Flag names capped at the engine's 31 characters

- **Cross-language story symbols** (`aet-eaw-edit.features.story.symbols`). See [#84](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/84).
  - Event names, flags and AI-notification ids indexed across XML and Lua
  - Go to definition from `Prereq` tokens and `TRIGGER_EVENT` parameters to the event; from `STORY_AI_NOTIFICATION` ids to the Lua `Story_Event("...")` call; `StoryModeEvents` keys and `Check_Story_Flag` arguments linked back
  - Typed story parameters (planets, units, factions, speech events) are full references with hover and validation; `None`/`null`/`Default` exempt

- **Story campaign graph diagnostics** (`aet-eaw-edit.features.story.graphDiagnostics`). See [#83](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/83).
  - Dangling or cyclic prerequisites
  - Duplicate event names in one file
  - Ambiguous campaign-global `TRIGGER_EVENT` targets
  - Events that can never fire
  - Suspended plots nothing activates
  - Deviations from the documented tag order
  - Flag names over 31 characters

- **Campaign story-chain discovery** (`aet-eaw-edit.features.story.discovery`). See [#82](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/82).
  - Campaigns, plot manifests and thread files followed from `CampaignFiles.xml` and typed
  - Story event and reward parameter validation and completion
  - Broken links (`*_Story_Name`, plot entries, tactical plots, malformed manifests) reported on the referencing line

- **Story-dialog language service** (`aet-eaw-edit.features.dialog.diagnostics`). See [#89](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/89).
  - Unknown commands, wrong argument counts and types, untested-command warnings
  - Reference checks: `TEXT`/`TITLE` keys, `DIALOG` speech events, `MOVIE`/`MOVIE_ONCE`, `SFX`
  - Scope declared in the `.pgproj` `directories.storyDialog` node
  - Story events cross-checked: unresolved `Story_Dialog`, `Story_Chapter` naming an undefined chapter

### Bug fixes

- **Go to definition on 36 more tags**, previously never indexed as references, now navigable and validated. Includes skirmish AI force lists, faction `Allies`/`Enemies`, `Preferred_Pathfinder_Types`, random-story unit lists, and bone, icon, map and localisation references. See [#77](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/77).

- **Go to definition on the SFX half of tuple sound tags** (`SFXEvent_Hardpoint_Destroyed`, `SFXEvent_Attack_Hardpoint`, GUI ability toggles). See [#78](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/78).

- **Inlay hints and code lenses refresh after localisation changes**: edits, added languages and on-disk changes now notify the editor. See [#45](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/45).

- **Additive tags keep their entries in *Show effective object***: each `Death_Clone` entry survives as its own element, base entries first, instead of a flattened, deduplicated value.

- **Multiple `<Prereq>` lines** on one story event are an OR of AND-groups, no longer reported as duplicates. See [#60](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/60).

- **Localisation editor icons** ship in the packaged VSIX; they were loaded from `node_modules`.

- **Hovering an XML comment** no longer logs a warning. See [#72](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/72).

## 0.2.0

### Breaking changes

- **`.pgproj` localisation moved out of `directories`.** `directories.text` and `directories.textResourceType` removed; new top-level `localisation` node:

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

  - `type`: `CSV`, `DAT`, `XML` or `NLS` (uppercase)
  - A `.pgproj` in the old shape fails to load with a notification naming the fix
  - Full steps, including clearing cached indexes: [Upgrading from 0.1.x](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/blob/master/PG.StarWarsGame.LSP.Client.VSCode/aet-eaw-edit/README.md#upgrading-from-01x)

- **Multiple `.pgproj` files under one workspace root fail startup** with a notification listing them, instead of picking one at random. Remove the extras or open the subfolder.

### Features

- **Feature flags** (`aet-eaw-edit.features.*`) for XML, Lua and cross-language tooling; any change restarts the server. Off by default while in development: Lua hover, Lua diagnostics, localisation tooling.
- **Text editor overhaul.** See [#55](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/55).
- **Import existing localisation projects** into a `.pgproj`. See [#56](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/56).
- **`.pgproj` localisation support for all formats.** See [#57](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/57).
- **`.pgproj` localisation merge chains.** See [#58](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/58).

### Lua / EmmyLua support

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
- `LuaApiSchemaProvider` parses `@class` and `@field`. See [#13](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/13).

### Bug fixes

- `Land_Terrain_Model_Mapping` format. See [#23](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/23).
- `Presence_Induced_Animations` format. See [#24](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/24).
- Valid behaviours no longer flagged. See [#25](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/25).
- `SurfaceFX_Name` content. See [#27](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/27).
- Valid 64-bit category masks no longer flagged. See [#28](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/28).
- Valid ability names no longer flagged. See [#30](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/30).
- `Hardpoint::Damage_Particles` no longer flagged. See [#38](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/38).
- Min/Max Pitch. See [#40](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/40).
- `MSS_3D_Provider_Name` duplicate-tag flagging. See [#41](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/41).
- Multiple "Defaults". See [#42](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/42).
- `Factions.xml` music events. See [#43](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/43).
- `Land_Skirmish_Unit_Cap_By_Player_Count`. See [#44](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/44).
- Invalid-but-valid `Damage_To_Armor_Mod`. See [#47](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/47).
- Necessary duplicate tags no longer flagged. See [#48](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/48).
- Base game `Damage_Type`. See [#49](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/49).
- `TALK` recognised as a valid animation. See [#50](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/50).
- `Map_Load_Spawn_Table` spawn probability. See [#51](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/51).

## 0.1.2

- Hotfix for URI encoding and decoding causing cache misses. See [#20](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/20).

## 0.1.1

First public preview release.

- XML: completions, hover, diagnostics, go to definition, find all references, rename, code actions, code lens
- Variant inheritance: Show Effective Object resolves `Variant_Of_Existing_Type` chains
- Lua script indexing and diagnostics
- Localisation editor panel (CSV, XML, DAT, Properties)
- Mod project file (`.pgproj`) with multi-project references
- Server version check on startup, with a download prompt on mismatch
