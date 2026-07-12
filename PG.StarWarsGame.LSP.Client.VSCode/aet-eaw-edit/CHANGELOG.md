# Changelog

## Unreleased

### Features

- Story simulator (opt-in via `aet-eaw-edit.features.tools.storySimulator`, builds on story discovery): press *▶ Simulate* in the story graph panel to run the campaign's story semantically — no game process. The virtual clock fires `STORY_ELAPSED` events, `TRIGGER_EVENT`-style rewards cascade through the graph, `SET_FLAG` rewards feed `STORY_FLAGS` watchers, `DISABLE_*` rewards disable their targets, and `STORY_ELEMENT` rewards activate suspended plots — node colours update live as lifecycles change. Everything the model can't decide becomes a manual intervention: a queue lists armed events waiting on you, Lua-linked `STORY_AI_NOTIFICATION` events offer the `Story_Event` ids collected from the campaign's scripts, and tactical outcomes resolve by firing the armed victory/defeat events. A flag inspector and step log round it out; simulation state is deterministic (same inputs, same story) and never touches your files. See [#90](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/90).

- Story graph editing (same `aet-eaw-edit.features.tools.storyEditor` opt-in as the viewer): the story graph panel is now an editor. Drag a connection between two events to add a prerequisite; the property panel edits everything in place — rename (campaign-wide, with ambiguity guard), event/reward types from the schema, params, branch, perpetual, dialog, and per-line prerequisite tokens with add/remove; *＋ Event* creates a new event in any thread of the campaign; *Delete event* asks first. Every change is a server-validated command that lands as a normal `workspace/applyEdit` — undo/redo (Ctrl+Z in the XML file) and open-editor sync just work. After an edit the graph updates in place: only affected nodes re-render, your pan/zoom position and node layout stay exactly where they were (the full auto-layout only runs on first open and filter changes). Edits are minimal (comments and formatting outside the touched lines are preserved byte-for-byte), edits are minimal (comments and formatting outside the touched lines are preserved byte-for-byte), and tags the editor inserts follow the engine's documented order. Files from a dependency or the base game are read-only — the editor tells you to copy them into your project first. Node positions you arrange by hand persist per campaign in `.aetswg/story-layout.json`. The protocol also carries thread/manifest management (create/detach/suspend threads, attach Lua scripts, attach/detach plot manifests, tactical attachments) for upcoming navigator UI. See [#88](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/88).

- Story-dialog navigation & hints: dialog `.txt` scripts gain inlay hints (opt-in via `aet-eaw-edit.features.dialog.inlayHints`) showing the referenced localisation text at the end of `TEXT`/`TITLE` lines — or a MISSING marker for unknown keys — and go-to-definition (opt-in via `aet-eaw-edit.features.dialog.goToDefinition`) jumping from `DIALOG`, `MOVIE`/`MOVIE_ONCE`, and `SFX` arguments to the defining XML object. Both apply only to files under the `.pgproj` `directories.storyDialog` folders.

- Story navigator & graph viewer (opt-in via `aet-eaw-edit.features.tools.storyEditor` + `aet-eaw-edit.features.story.discovery`): a new *EaWEdit: Story* activity-bar view lists every campaign → faction → plot threads and attached Lua scripts (suspended threads marked; clicking opens the file). The graph icon on a campaign — or the *EaWEdit: Open Story Graph* command — opens a read-only graph panel: auto-laid-out event flow with AND/OR junctions, cross-file portals and tactical plots, node colours by lifecycle (inactive/waiting/armed/fired/disabled), dimmed unreachable events, and dashed borders on schema-untested event/reward types. A toolbar filters by name, branch and lifecycle; selecting an event shows its full property view with *Open XML* (jumps to the event block) and *Reachable from here* (trims the graph to everything downstream). The view refreshes live as story files are edited. See [#87](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/87).

- Cross-language story rename (opt-in via `aet-eaw-edit.features.story.rename`, builds on story symbols): renaming a story event, flag, or AI-notification id — from XML or from Lua — updates the definition and every reference across story threads and scripts in one workspace edit. Guard rails match engine semantics: an event name defined more than once in the workspace is rejected instead of mass-renamed (disambiguate first), and story flag names are capped at the engine's 31-character limit. See [#85](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/85).

- Story editor protocol surface (opt-in via `aet-eaw-edit.features.tools.storyEditor`, builds on story discovery): the server now answers `aet/getStoryPlots` (campaign → faction → plot-thread tree), `aet/getStoryGraph` (per-campaign event graph with lifecycle states and name/branch/lifecycle/reachable-from filters), `aet/getStoryNodeDetail` (full event payload), and `aet/getStorySchema` (event/reward type catalogue), and pushes a debounced `aet/storyGraphChanged` notification when edits invalidate a campaign model. This is the data backbone for the upcoming story graph editor; the requests carry a friendly error naming the exact setting to flip when disabled. See [#86](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/86).

- Cross-language story symbols (opt-in via `aet-eaw-edit.features.story.symbols`, builds on story discovery): story event names, flags, and AI-notification ids are indexed across XML and Lua. Go-to-definition works from a `Prereq` token or `TRIGGER_EVENT` parameter to the event block, and from a `STORY_AI_NOTIFICATION` id straight to the Lua `Story_Event("…")` call that fires it; `StoryModeEvents` table keys and `Check_Story_Flag` arguments are linked back too. Story event/reward parameters whose schema names a real object type (planets, units, factions, speech events) are full references too: Ctrl+Click jumps to the defining XML, hover works, and unknown values are flagged by the same validation the rest of the workspace uses — including the engine-placeholder exemption (`None`/`null`/`Default` are no longer false positives). See [#84](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/84).

- Story campaign graph diagnostics (opt-in via `aet-eaw-edit.features.story.graphDiagnostics`, builds on story discovery): story thread files are analysed as part of their whole campaign — dangling or cyclic prerequisites, duplicate event names in one file, ambiguous campaign-global event targets (`TRIGGER_EVENT` resolves campaign-wide), events that can never fire, suspended plots that nothing activates, deviations from the documented event tag order, and flag names over the engine's 31-character limit. See [#83](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/83).

- Campaign story-chain discovery (opt-in via `aet-eaw-edit.features.story.discovery`): campaigns, story plot manifests, and story thread files are followed from `CampaignFiles.xml` and typed, activating story event/reward parameter validation and completion in story files. Broken links in the chain (a `*_Story_Name` or plot entry pointing at a missing file, tactical plot references, malformed manifests) are reported as diagnostics on the referencing line. See [#82](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/82).
- Story-dialog language service (opt-in via `aet-eaw-edit.features.dialog.diagnostics`): dialog `.txt` scripts get diagnostics — unknown commands, wrong argument counts and types, warnings for documented-but-untested commands, and reference checks for localisation keys (`TEXT`/`TITLE`), speech events (`DIALOG`), movies (`MOVIE`/`MOVIE_ONCE`) and sound events (`SFX`). Which `.txt` files are dialog scripts is declared in the `.pgproj` via the new `directories.storyDialog` node — filename conventions play no part. Story events cross-check too: a `Story_Dialog` that doesn't resolve inside the declared scope and a `Story_Chapter` pointing at a chapter the script doesn't define are flagged. See [#89](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/89).

### Bug fixes

- Localisation editor icons (the search-mode toggles and the reset-to-inherited gutter arrows) now appear in the packaged extension. They were loaded from `node_modules`, which is not part of the published VSIX, so they only showed up when running the extension from source.

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

- Feature flags: independently enable or disable XML, Lua, and cross-language tooling capabilities via new `aet-eaw-edit.features.*` settings. Changing any flag automatically restarts the language server. Lua hover, Lua diagnostics, and the localisation tooling (editor panel, initialise/import commands, create-key code action) ship disabled by default while still in development — enable the corresponding setting to opt in early.
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
