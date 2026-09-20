# Alamo Engine Tools - Empire at War Edit

Editor support for **Star Wars: Empire at War** and **Forces of Corruption** mod development.

- XML and Lua: completions, hover documentation, cross-file diagnostics, navigation, rename
- Story tooling: campaign navigator, story graph, cross-language symbols
- Localisation editor
- Model preview
- Lua debugger

> **Preview release.** Not every feature is complete; behaviour may change between versions. Report problems on the [issue tracker](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues).

---

## Requirements

- Visual Studio Code 1.107 or later
- Windows 11 (x64); Windows 10 is unsupported (end of mainstream support October 2025)
- The PG.StarWarsGame.LSP server binary, self-contained; no .NET runtime needed

---

## Getting started

### 1. Download the server

From the [Releases](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/releases) page:

- `aet-eaw-edit-x.x.x.vsix` - the extension, for manual installs
- `PG.StarWarsGame.LSP.Server-x.x.x-win-x64.zip` - the language server; extract to a permanent folder, e.g. `C:\tools\aet-lsp\`

### 2. Configure the extension

| Setting | Value |
|---|---|
| `aet-eaw-edit.lsp.executable` | Full path to `PG.StarWarsGame.LSP.Server.exe` |
| `aet-eaw-edit.lsp.enabled` | `true` |

Activation: an open XML or Lua file, or a `.pgproj` in the workspace. A missing or mismatched server binary is reported with a link to the matching release.

### 3. Create a mod project file

`.pgproj` at the root of the mod workspace:

```json
{
  "_type": "aetswg.ModProject",
  "_typeVersion": "aetswg-1.0.0",
  "name": "My Mod",
  "directories": {
    "xml":     ["data/xml"],
    "scripts": ["data/scripts"],
    "art":     ["data/art"],
    "audio":   ["data/audio"]
  },
  "localisation": {
    "type": "CSV",
    "directory": "data/text"
  },
  "projectReferences": []
}
```

- `_type` and `_typeVersion` - the file's format, written and maintained by the extension. A file without them loads as format 1.0.0; a newer format than the extension understands is refused.
- Paths are relative to the `.pgproj`.
- `projectReferences` - other `.pgproj` files whose symbols the mod inherits, e.g. a base game project.
- `localisation.type` - `CSV`, `DAT`, `XML` or `NLS`.
- `icons` (optional) - `megaTexture` (atlas pair without extension, e.g. `data/art/textures/mt_commandbar`) and `sourceRoots` (loose icon folders). Omitted: engine convention.

---

## Upgrading from 0.1.x

**Required `.pgproj` change.** Localisation moved from `directories` to its own node:

```diff
   "directories": {
     "xml": ["data/xml"],
     "scripts": ["data/scripts"],
     "art": ["data/art"],
-    "audio": ["data/audio"],
-    "text": ["data/text"],
-    "textResourceType": "Csv"
+    "audio": ["data/audio"]
   },
+  "localisation": {
+    "type": "CSV",
+    "directory": "data/text"
+  },
   "projectReferences": []
```

A `.pgproj` in the old shape fails to load with a notification naming this fix.

**One `.pgproj` per workspace.** More than one fails to start with a notification listing them all; remove the extras or open the subfolder holding the one you want.

**Clear cached indexes.** Delete `.aetswg` next to every `.pgproj` and `%USERPROFILE%\.aetswg\`, then run **EaWEdit: Restart LSP Server**. Both are rebuilt automatically.

---

## XML features

Indexes the full EaW/FoC object graph across the workspace.

- **Completions** - enum values and named objects from the whole workspace
- **Hover** - type, description and valid values for tags, enum values and references
- **Diagnostics**
  - Unknown references
  - Unknown or misspelled tags, with a suggestion
  - Unnamed objects, missing required tags
  - Type mismatches, malformed values, deprecated fields
  - Duplicate declarations
- **Engine rules** - about 150 checks taken from the game's parser and error messages
  - Numeric ranges
  - Tags required or forbidden together
  - Comparisons between tags of one object
  - Variant-inheritance rules
- **Go to definition** - `F12` / `Ctrl+Click`, across files
- **Find all references** - `Shift+F12`
- **Rename** - `F2`, workspace-wide
- **Code actions** - quick fixes, including creating a missing localisation key
- **Code lens** - reference counts above every named object
- **Auto-close tags** - on `>` (needs `editor.formatOnType`)
- **Linked editing** - opening and closing tag renamed together (needs `editor.linkedEditing`)
- **Variant inheritance** - **Show Effective Object** opens the merged XML of a `Variant_Of_Existing_Type` object

---

## Lua features

Scripts under the declared `scripts` directories are indexed and checked.

- Completion
- Go to definition
- Rename, including XML objects referenced from Lua
- Code lenses, inlay hints, quick fixes
- Hover and diagnostics _(work in progress)_

---

## Lua debugger

> **Work in progress, off by default.** Flag: `aet-eaw-edit.features.lua.debugger`. Requires a debug build of the game with its Lua debug server running; retail builds have none.

Debug type **Empire at War Lua** in Run and Debug:

- Breakpoints in Lua files under the project's script directories
- Attach to a running game, or launch it with the mod chain
- Call stack, locals in the Variables view, watches and hovers on plain names
- Debug Console running Lua in the selected or stopped script
- **Lua Scripts** view: every running script instance with its coroutine threads; break in a script or a thread (the game cannot start a script on request)

`F5` in a Lua file attaches with the settings below. `launch.json`:

```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "type": "eaw-lua",
      "request": "attach",
      "name": "Attach to Empire at War (Lua)"
    },
    {
      "type": "eaw-lua",
      "request": "launch",
      "name": "Launch Empire at War (Lua)",
      "program": "${config:aet-eaw-edit.game.executable}"
    }
  ]
}
```

| Attribute | Request | Default | Description |
|---|---|---|---|
| `host` | both | `aet-eaw-edit.game.luaDebugHost` | Machine running the game |
| `port` | both | `aet-eaw-edit.game.luaDebugPort` | The game's Lua debug UDP port |
| `sourceRoots` | both | every project layer's script directories | Directories the game's script paths map onto, mod first |
| `clientName` | both | `AetLuaDebugger:<pid>` | Client name the game logs |
| `unsafeTableExpansion` | both | `aet-eaw-edit.game.unsafeTableExpansion` | Expand tables in the Variables view |
| `dropDuplicateOutermostFrame` | both | `true` | Drop a duplicated outermost call-stack entry |
| `program` | launch | `aet-eaw-edit.game.executable` | Debug game executable |
| `args` | launch | `[]` | Extra arguments, after the mod chain |
| `cwd` | launch | executable's directory | Working directory |
| `modPaths` | launch | built from the project layers | `MODPATH=` entries, leaf first; replaces the chain from the `.pgproj` |
| `attachTimeoutSeconds` | launch | `60` | Attach retry window after launch |

**Launch and the mod chain**

- One `MODPATH=` per project layer: mod first, dependencies after
- The game reads a mod folder as-is, so each layer needs:
  - Its declared directories under its `Data/` folder
  - No space in its path
- A layer that does not comply is refused with the reason
- `modPaths` overrides the chain
- A build step for other layouts is planned ([#144](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/144))

**Lua Scripts view**

- Refresh reloads the list
- Expanding a script fetches its threads
- Break actions are greyed out, with the reason in the tooltip, while a script is stopped or a break is already waiting

**Limits of the game's debug server**, reported rather than hidden:

- Breakpoint conditions, hit counts and log messages are never evaluated; such breakpoints are reported as not set
- The whole game freezes while a script is stopped
- Pause takes effect at the next Lua line of the selected script, or of any attached script
- Locals are the names found in scope at the stopped line
  - Upvalues resolve to the global of the same name
  - Numbers are reported as integers
- Tables show as `table: 0x...` without children unless `aet-eaw-edit.game.unsafeTableExpansion` is on
  - The game is reported to crash on member text of 255 bytes or more

---

## Suppressing diagnostics

Every diagnostic carries a stable id `aetswg-<group>-<number>` (e.g. `aetswg-004-0001`), shown in the Problems panel's Code column.

### Quick fixes

`Ctrl+.` on a problem offers four scopes, narrowest first, none preselected:

- **Suppress ... for this line**
- **Suppress ... for this `<Unit>` / function / chapter** - only with an enclosing declaration
- **Suppress ... in this file**
- **Suppress ... across the project**

### Writing directives by hand

| Language | Comment form |
|---|---|
| XML | `<!-- aetswg:suppress aetswg-004-0001 -->` |
| Lua | `-- aetswg:suppress aetswg-004-0001` |
| Story dialog (`.txt`) | `# aetswg:suppress aetswg-004-0001` |

| Keyword | Covers |
|---|---|
| `aetswg:suppress` | the next element (XML), statement (Lua), or command line (dialog) |
| `aetswg:suppress-object` | the enclosing object element, function, or `[CHAPTER n]` section |
| `aetswg:suppress-file` | the whole file |

A directive may share a longer comment. A scope with no target (an `-object` directive outside any object, a trailing directive) covers its own line only.

Project-wide suppressions live in `.aetswg/suppressions.json`, written by the **across the project** quick fix. Commit that file.

### Lists, wildcards, and reasons

Several ids per directive, comma-separated; a note after `reason::`:

```xml
<!-- aetswg:suppress aetswg-010-0002, aetswg-010-0003 reason:: deliberate shadow, cleared with the art team -->
```

`*` in place of the number silences a group, e.g. `aetswg-004-*` for every asset-file check:

| Group | Covers |
|---|---|
| `aetswg-001-*` | References that do not resolve |
| `aetswg-002-*` | Enum values |
| `aetswg-003-*` | Value format: numbers, booleans, tuples, arity |
| `aetswg-004-*` | Asset files: models, textures, audio, maps |
| `aetswg-005-*` | Localisation keys |
| `aetswg-006-*` | Document structure |
| `aetswg-007-*` | Cross-tag rules within one object |
| `aetswg-008-*` | Variant inheritance |
| `aetswg-009-*` | Story and campaigns |
| `aetswg-010-*` | Symbols and layers: duplicates, shadowing |
| `aetswg-011-*` | Engine constraints |
| `aetswg-012-*` | Syntax errors |
| `aetswg-013-*` | Suppression comments themselves |

Groups classify the problem, not the language: `aetswg-001-*` covers XML and Lua alike.

### When a directive is wrong

- `aetswg-013-0001` - an entry that is not an id or wildcard (typo, or missing leading zeros: `aetswg-4-1` for `aetswg-004-0001`)
- `aetswg-013-0002` - a directive naming no diagnostic

Only the bad entry is rejected; the rest of the list applies. Silence these with `aetswg-013-*`.

### `<Override>` is not a suppression

`<!-- <Override Name="..."/> -->` declares intended shadowing of a lower layer, a claim the server checks. Unaffected by suppression.

Dialog quick fixes require `aet-eaw-edit.features.dialog.codeActions`; the rest of suppression is always on.

---

## Story mode

Campaigns are followed from `CampaignFiles.xml` through plot manifests to the `Story_*.xml` thread files and understood as one graph. Opt-in and in development; see the story flags under [feature flags](#feature-flags).

- **Campaign Editor view**
  - Campaigns, factions, galactic plot threads, the faction's battles in play order with their own plot files beneath them, and attached Lua scripts
  - Suspended threads marked
  - Click opens the file; a battle opens its graph
- **Story graph** - one panel per campaign faction at the galactic level, and one per battle
  - Auto-laid-out event flow with AND/OR junctions and cross-file portals
  - **Battles as sub-graphs**: the galactic graph shows each tactical mission as one portal that opens the battle's own panel; the battle graph shows the galactic events that link it in and listen for its outcome as portals back, each opening the galactic panel on that event. The command palette lists the battles after their faction
  - Node colour by lifecycle: inactive, waiting, armed, fired, disabled
  - Unreachable events dimmed; schema-untested types dashed
  - Filters: name, branch, lifecycle, plot state, the incoming or outgoing chain of one event - inside the panel's scope; the chain runs through a battle's portal
  - **Engine links** (dash-dot): the links the game makes that no prerequisite writes - a speech or movie to the listener that waits for it to end, a battle to the galactic listeners for its outcome and for the summary dialog closing. Drawn so a sequence the game plays in order reads in order; never a prerequisite
  - Thread lanes on the galactic graph (Empire at War's acts); disabled inside a battle, which is one plot
- **Colour key** - flyout keying node, border and edge styles, including per-branch hues on zoomed-out nodes
- **Editing** (Edit mode)
  - Drag a prerequisite between events
  - Drop event and reward types from the palette
  - Edit names, params, branch, perpetual and dialog in place
  - Staged and applied on save as one `workspace/applyEdit`; minimal edits; inserted tags in the engine's order
- **Simulation** (Simulation mode, flag `tools.storySimulator`)
  - The campaign runs forward in 1 s ticks by the engine's own event rules: push-based arming, campaign-wide trigger rewards, resets, disables, timers from arming, flag comparisons with the engine's defaults, speech and movie completions owed to the next tick
  - The world is a fact table seeded from the campaign XML - planets and owners, units, tech, credits - written only by rewards and by you; nothing moves, builds or fights on its own
  - Every trigger the game would decide is a **decision** for you: capture a planet, build or destroy a unit, win or lose a battle, send a script event, or assume the trigger met; pickers list what fits the facts first and accept any name
  - **Battles run as their own session**, as the game plays them: the galactic simulation arms the galactic listeners only. Entering a battle - from its portal, its row in the dock, or the decision that waits on it - opens its panel into Simulation on its own clock, seeded with the galaxy's flags and world, and the galaxy stands still until it resolves. Won or lost, from either panel: the battle's own listeners fire, the flags it wrote cross into the galaxy, and the galactic outcome listeners fire. A rewind replays the outcome as one step without replaying the battle
  - Campaign scripts run as their `PGStateMachine`: a fired event enters the state, its `OnEnter` thread sleeps and sends `Story_Event` ids back into the graph, drawn as state nodes and links
  - Dock header: the tick and what stopped the clock; content: decisions, world, flags, scripts, each row opening beside the dock; foot: the transport - restart, one tick back, play, one tick, run until the story reaches a new decision, a breakpoint or nothing more can happen - with a pace of pulse, step or a custom rate. The clock is never held back by open decisions: a real campaign has hundreds of armed listeners from tick 0
  - Canvas: the flow along the edges, fire counts, gate meters on armed timers and flag checks, breakpoint marks; lenses for the path taken, the flow tints and the script states
  - Trace panel: every transition with its tick, node, cause and source; filter by text or `t12` for one tick; copy
  - Breakpoints on any event and on every clock or flag gate; rewind to any tick replays the same answers
- **Large campaigns** - lightweight overview when zoomed out, real nodes only for the visible part; 1000+ events stay responsive
- **Campaign diagnostics**
  - Dangling or cyclic prerequisites
  - Duplicate event names
  - Prerequisites, reset and disable targets the engine cannot see because they live in another plot file
  - Two events of one name in one plot file
  - Events that can never fire
  - Suspended plots nothing activates
  - Problems bar can follow the branch filter; jumping to a hidden problem lifts it
- **Cross-language symbols**
  - Event names, flags and AI-notification ids indexed across XML and Lua
  - Go to definition from `Prereq` tokens and `STORY_AI_NOTIFICATION` ids to the Lua `Story_Event("...")` call
  - `F2` renames across threads and scripts
- **Dialog scripts** (`.txt` scripts declared in the `.pgproj`)
  - Diagnostics
  - Inlay hints with the localisation text of `TEXT` and `TITLE` lines
  - Go to definition on speech, movie and sound arguments

Dependency and base-game files are read-only. Node positions, events and junctions, portals and script states alike, persist per campaign faction in `.aetswg/story-layout.json`.

---

## Model preview

Flag: `aet-eaw-edit.features.tools.modelPreview` (on by default).

- **Alamo Model Preview** editor for `.alo` models and `.ala` animations; the file only, no XML
- **Preview Assembled Unit** (command and GameObject code lens) - the unit as the game builds it
  - Hardpoints on their attachment bones
  - Movable turrets
  - Firing arcs
  - Team colour
- **Model lens**
  - Model tree with per-part visibility
  - LOD and ALT levels
  - Skeleton, cameras, info inspector
  - Scene and light settings
- **Animation lens**
  - Clip library by family, action and take
  - The game's loop rule: idle repeats, death does not
  - Transport, playback speed
- **Gameplay lens**
  - Attacker weapons and abilities against the unit
  - Shields and hardpoint health
  - Destroy and repair hardpoints, with damage smoke and death clone
  - Damage log naming the armour type per hit
- **Particles** - effects, groups and emitters of a particle system
- **Saved shots** - captures kept in a set
- **Handoff** - *Open in AloViewer* and *Open in Particle Editor* via `aet-eaw-edit.tools.aloViewerExecutable` and `aet-eaw-edit.tools.particleEditorExecutable`
- **Shaders** - **Set Up Base Shader Sources** fetches Petroglyph's published shader sources into `aet-eaw-edit.shaders.directory`; never redistributed
- **Game models** - require `aet-eaw-edit.lsp.source.baseGameDirectory` and, for Forces of Corruption, `expansionDirectory`
- **Energy pool** - behind `aet-eaw-edit.features.preview.energyPool`, off on purpose: the engine implements it, the shipped game disables it
- **Not covered** - sounds and animation SFX maps; AloViewer remains the tool for those

---

## Encyclopedia preview

Flag: `aet-eaw-edit.features.tools.encyclopedia` (on by default). Read-only.

- **Preview Encyclopedia Popup** (command and GameObject code lens) - the in-game tooltip card
  - Icon, name, class line
  - `Encyclopedia_Text` wrapped as the game wraps it
  - Ship names from the game's tables
  - Faction switch
- **Language** - `aet-eaw-edit.lsp.localisation.language` (the game's language, not the extension's)
- **Icons** - from the mega texture
  - Game icons baked into the baseline
  - A project's own `mt_commandbar` (`.pgproj` `icons`) replaces them wholesale
  - Missing everywhere: error
  - In sources but not in the mega texture: warning to rebuild

---

## Localisation editor

Flag: `aet-eaw-edit.features.tools.localisation`. **Localisation Editor** activity bar view, files grouped into **Text files** and **Credits files**; one grid tab per file, splittable side by side.

- **Editing** - staged edits, count on **Save**, one all-or-nothing write; refused if the file changed on disk
- **Validate** - without writing; duplicate and empty keys are errors in text files, expected in credits files
- **Rows** - right-click menu: add and delete; reorder on credits files
- **Add language** - from the game's language list, for formats that hold more than one
- **Fill language** - from another language or from the game's own translations
- **Inherited rows** - hidden by default, **Inherited** toggle shows them; **Reset to inherited value** on overridden rows
- **Filter** - plain text, wildcard (`*`, `?`) or regex; scoped to keys, one language or everything
- **Columns** - language columns chosen from the header gear; empty languages hidden at first
- **Export to DAT** and **Convert to another format** - from the file's context menu; conversion keeps the original
- **New project** - initialise from the EaW + FoC baseline, or import existing files

Saves rewrite changed rows only; untouched rows keep quoting, comments and line endings.

### Credits files

Ordered lists that may repeat a key. Rows are addressed by position, so duplicates, spacer rows and order survive editing.

- Line kinds, offered as tiles to drag into the table:
  - `HEADER` - label
  - `CENTER` - name
  - `[TBL]` - blank spacer
- **Open Credits Preview to the Side** and **Play Credits Crawl Full Screen** play the staged rows as the game does

Detection: engine naming (`credits*`), or in the `.pgproj`:

```jsonc
"localisation": {
  "type": "CSV",
  "directory": "data/text",
  "credits": {
    // "convention" (default), "explicit" (only the files listed), or "none"
    "detection": "convention",
    "files": ["rolls.csv"]
  }
}
```

One format per project: a CSV project's credits file is a `.csv`. `"type": "DAT"` projects edit compiled `.dat` files directly (language from the file name, sort order preserved). **Set Localisation Project Format** changes the format from the editor.

---

## Commands

Command Palette (`Ctrl+Shift+P`). Commands of a flagged feature are greyed out until the flag is on.

| Command | Description | Flag |
|---|---|---|
| EaWEdit: New Mod Project | Creates a `.pgproj` and initial directories | - |
| EaWEdit: Reload Mod Project | Re-reads the `.pgproj`, re-indexes | - |
| EaWEdit: Re-validate Workspace | Re-runs diagnostics in every language | - |
| EaWEdit: Restart LSP Server | Restarts the language server | - |
| EaWEdit: Show Effective Object (Variant Inheritance) | Merged XML of a variant object | `tools.variants` |
| EaWEdit: Preview Model | Opens a model by file name, without XML | `tools.modelPreview` |
| EaWEdit: Preview Assembled Unit | Opens the GameObject under the cursor, assembled from the XML | `tools.modelPreview` |
| EaWEdit: Set Up Base Shader Sources | Fetches the shader sources into `aet-eaw-edit.shaders.directory` | `tools.modelPreview` |
| EaWEdit: Preview Encyclopedia Popup | The encyclopedia card of the GameObject under the cursor | `tools.encyclopedia` |
| EaWEdit: Open Story Graph | A faction's story graph | `tools.storyEditor`, `story.discovery` |
| EaWEdit: Refresh Story Navigator | Rebuilds the campaign tree | `tools.storyEditor`, `story.discovery` |
| EaWEdit: New Localisation Project | Initialise from baseline, or import | `tools.localisation` |
| EaWEdit: Initialise Localisation Project from Baseline | Starter file from the game baseline | `tools.localisation` |
| EaWEdit: Import Existing Localisation Files | Adopts CSV, XML, Properties or DAT files | `tools.localisation` |
| EaWEdit: Set Localisation Project Format | Writes `localisation.type` | `tools.localisation` |
| EaWEdit: Open Localisation Editor | Grid tab for a file | `tools.localisation` |
| EaWEdit: Open Localisation File as Text | Plain text editor for a file | `tools.localisation` |
| EaWEdit: Refresh Localisation Files | Re-reads the project's files | `tools.localisation` |
| EaWEdit: Convert Localisation File to Another Format | CSV, XML, Properties or DAT | `tools.localisation` |
| EaWEdit: Export Localisation to DAT | Compiled `.dat` for a file | `tools.localisation` |
| EaWEdit: Open Credits Preview to the Side | Credits crawl beside the editor | `tools.localisation` |
| EaWEdit: Play Credits Crawl Full Screen | Credits crawl over the editor | `tools.localisation` |
| EaWEdit: Refresh Lua Scripts | Reloads the Lua Scripts view from the game | `lua.debugger`, active session |
| EaWEdit: Refresh Script Threads | Reloads one script's threads (inline) | `lua.debugger`, active session |
| EaWEdit: Break in Script | Break at the script's next Lua line (inline) | `lua.debugger`, game running |
| EaWEdit: Break in Thread | Break at the thread's next Lua line (inline) | `lua.debugger`, game running |

Flag column: the feature-flag id without its common prefix.

---

## Settings reference

### Language server

| Setting | Default | Description |
|---|---|---|
| `aet-eaw-edit.lsp.enabled` | `false` | Enable the language server |
| `aet-eaw-edit.lsp.executable` | _(empty)_ | Path to `PG.StarWarsGame.LSP.Server.exe` |
| `aet-eaw-edit.lsp.locale` | `en` | Language of hover text and diagnostics (`en`, `de`, `fr`, `es`, `it`, `pl`, `ru`) |
| `aet-eaw-edit.lsp.localisation.language` | `ENGLISH` | Game language for displayed localisation text (hovers, inlay hints, encyclopedia card) |
| `aet-eaw-edit.lsp.debug.traceServer` | `off` | `messages` or `verbose`: LSP traffic in the EaWEdit output channel |

### Game installation and external tools

Never searched for; used only once set.

| Setting | Default | Description |
|---|---|---|
| `aet-eaw-edit.lsp.source.baseGameDirectory` | _(empty)_ | Empire at War install; models, textures and string table that ship with the game |
| `aet-eaw-edit.lsp.source.expansionDirectory` | _(empty)_ | Forces of Corruption install, searched alongside the base game |
| `aet-eaw-edit.tools.aloViewerExecutable` | _(empty)_ | `AloViewer.exe`, for *Open in AloViewer* |
| `aet-eaw-edit.tools.particleEditorExecutable` | _(empty)_ | `ParticleEditor.exe`, for *Open in Particle Editor* |
| `aet-eaw-edit.shaders.directory` | _(empty)_ | Base game shader sources (`.fx`) for the preview; filled by *Set Up Base Shader Sources* or your own copy |
| `aet-eaw-edit.shaders.sourceUrl` | _(empty)_ | Download location for *Set Up Base Shader Sources*; empty = Petroglyph's published download |
| `aet-eaw-edit.modVerify.enabled` | `false` | ModVerify integration |
| `aet-eaw-edit.modVerify.executable` | _(empty)_ | [ModVerify](https://github.com/AlamoEngine-Tools/ModVerify/releases) executable |

### Schema

The EaW/FoC XML schema, fetched from GitHub on server start and cached with ETags; a local copy for offline or pinned use.

| Setting | Default | Description |
|---|---|---|
| `aet-eaw-edit.lsp.schema.source` | `http` | `http` (GitHub) or `local` |
| `aet-eaw-edit.lsp.schema.localPath` | _(empty)_ | Local `schema/eaw/` directory (source `local`) |
| `aet-eaw-edit.lsp.schema.url` | _(empty)_ | Custom schema index URL (source `http`) |

### Baseline

Snapshot of all vanilla EaW and FoC objects and localisation keys, downloaded once to `%USERPROFILE%\.pg-swg-lsp\baselines\` and refreshed on new versions. Powers reference validation and the Inherited toggle.

| Setting | Default | Description |
|---|---|---|
| `aet-eaw-edit.lsp.source.baseline.type` | `http` | `http`, `local` or `none` |
| `aet-eaw-edit.lsp.source.baseline.localPath` | _(empty)_ | Local baseline file (type `local`) |
| `aet-eaw-edit.lsp.source.baseline.url` | _(empty)_ | Custom baseline URL (type `http`) |

### Development

| Setting | Default | Description |
|---|---|---|
| `aet-eaw-edit.lsp.devMode.enabled` | `false` | Start the server from source with `dotnet run --project` |
| `aet-eaw-edit.lsp.devMode.projectPath` | _(empty)_ | `PG.StarWarsGame.LSP.Server.csproj` for dev mode |
| `aet-eaw-edit.lsp.debug.waitForDebugger` | `false` | Start the server with `--wait-for-debugger` |

### Localisation editor

| Setting | Default | Description |
|---|---|---|
| `aet-eaw-edit.localisation.format` | `format-dat` | Default format for new localisation projects (`format-dat`, `format-csv`, `format-xml`) |

### Lua debugger

Read only with `aet-eaw-edit.features.lua.debugger` on; a `launch.json` attribute overrides the setting of the same meaning.

| Setting | Default | Description |
|---|---|---|
| `aet-eaw-edit.game.executable` | _(empty)_ | Debug `StarWarsI.exe` for launch; retail builds have no debug server |
| `aet-eaw-edit.game.arguments` | `[]` | Extra game arguments, before the mod chain |
| `aet-eaw-edit.game.luaDebugHost` | `127.0.0.1` | Machine running the game (UDP) |
| `aet-eaw-edit.game.luaDebugPort` | `1234` | Lua debug UDP port; the game takes the first free port from 1234 upward |
| `aet-eaw-edit.game.unsafeTableExpansion` | `false` | Expand tables in the Variables view; the game is reported to crash on member text of 255 bytes or more |

### Story simulation

| Setting | Default | Meaning |
| --- | --- | --- |
| `aet-eaw-edit.storySimulator.assumeMediaCompletes` | `true` | A speech or movie a reward starts completes on the next tick, so its `STORY_SPEECH_DONE` or `STORY_MOVIE_DONE` listener fires on its own. Off, each is a decision until answered or until the engine's 60 s timeout. Takes effect when a simulation starts |
| `aet-eaw-edit.storySimulator.autoResume` | `true` | When play paused itself for a decision, answering it resumes play |

### Feature flags

Every feature has a flag. Work-in-progress features default to off. A flag the language server reads restarts it on change; editor-side flags apply at once.

XML:

| Setting | Default | Description |
|---|---|---|
| `aet-eaw-edit.features.xml.completion` | `true` | Code completion |
| `aet-eaw-edit.features.xml.hover` | `true` | Hover tooltips |
| `aet-eaw-edit.features.xml.diagnostics` | `true` | Diagnostics |
| `aet-eaw-edit.features.xml.goToDefinition` | `true` | Go to definition |
| `aet-eaw-edit.features.xml.findReferences` | `true` | Find all references |
| `aet-eaw-edit.features.xml.rename` | `true` | Symbol rename |
| `aet-eaw-edit.features.xml.codeLens` | `true` | Code lenses (reference counts, variant links) |
| `aet-eaw-edit.features.xml.inlayHints` | `true` | Inlay hints |
| `aet-eaw-edit.features.xml.codeActions` | `true` | Code actions (quick fixes) |
| `aet-eaw-edit.features.xml.linkedEditing` | `true` | Linked editing of tag pairs (needs `editor.linkedEditing`) |
| `aet-eaw-edit.features.xml.autoCloseTag` | `true` | Auto-close tag on `>` (needs `editor.formatOnType`) |

Lua:

| Setting | Default | Description |
|---|---|---|
| `aet-eaw-edit.features.lua.completion` | `true` | Code completion |
| `aet-eaw-edit.features.lua.hover` | `false` | Hover tooltips _(work in progress)_ |
| `aet-eaw-edit.features.lua.diagnostics` | `false` | Diagnostics _(work in progress)_ |
| `aet-eaw-edit.features.lua.goToDefinition` | `true` | Go to definition |
| `aet-eaw-edit.features.lua.rename` | `true` | Symbol rename, including XML objects referenced from Lua |
| `aet-eaw-edit.features.lua.codeLens` | `true` | Code lenses |
| `aet-eaw-edit.features.lua.inlayHints` | `true` | Inlay hints |
| `aet-eaw-edit.features.lua.codeActions` | `true` | Code actions (quick fixes) |
| `aet-eaw-edit.features.lua.debugger` | `false` | Lua debugger: debug type, Lua Scripts view, `aet-eaw-edit.game.*` _(work in progress)_ |

Story mode:

| Setting | Default | Description |
|---|---|---|
| `aet-eaw-edit.features.story.discovery` | `false` | Follows the campaign story chain and types its files; base of every other story flag _(work in progress)_ |
| `aet-eaw-edit.features.story.graphDiagnostics` | `false` | Whole-campaign analysis: dangling and cyclic prerequisites, duplicate event names, targets outside their plot file, unreachable events, orphaned suspended plots, tag order, over-long flag names _(work in progress)_ |
| `aet-eaw-edit.features.story.symbols` | `false` | Story event names, flags and AI-notification ids indexed across XML and Lua _(work in progress)_ |
| `aet-eaw-edit.features.story.rename` | `false` | Cross-language rename of story symbols; builds on story symbols _(work in progress)_ |

Story dialog (`.txt` files under `directories.storyDialog` only):

| Setting | Default | Description |
|---|---|---|
| `aet-eaw-edit.features.dialog.diagnostics` | `false` | Unknown commands, argument errors, untested commands, reference checks _(work in progress)_ |
| `aet-eaw-edit.features.dialog.inlayHints` | `false` | Localisation text after `TEXT` and `TITLE` lines _(work in progress)_ |
| `aet-eaw-edit.features.dialog.goToDefinition` | `false` | From `DIALOG`, `MOVIE`/`MOVIE_ONCE` and `SFX` arguments to the XML object _(work in progress)_ |
| `aet-eaw-edit.features.dialog.codeActions` | `false` | Suppression quick fixes in dialog scripts _(work in progress)_ |

Tools:

| Setting | Default | Description |
|---|---|---|
| `aet-eaw-edit.features.tools.localisation` | `false` | Localisation editor, initialise and import commands, create-key code action _(work in progress)_ |
| `aet-eaw-edit.features.tools.storyEditor` | `false` | Campaign Editor view and story graph in View mode; builds on `story.discovery` _(work in progress)_ |
| `aet-eaw-edit.features.tools.storyEditing` | `false` | Edit mode in the story graph; builds on `tools.storyEditor` _(work in progress)_ |
| `aet-eaw-edit.features.tools.storySimulator` | `false` | Simulation mode in the story graph; builds on `tools.storyEditor` |
| `aet-eaw-edit.features.tools.variants` | `true` | Show Effective Object and its code lens |
| `aet-eaw-edit.features.tools.modelPreview` | `true` | Model preview; game models need `aet-eaw-edit.lsp.source.baseGameDirectory` |
| `aet-eaw-edit.features.tools.encyclopedia` | `true` | Encyclopedia popup preview and its code lens |
| `aet-eaw-edit.features.preview.energyPool` | `false` | Energy pool in the preview (editor-side, no restart). Off on purpose: the shipped game disables the mechanic |

---

## What this extension downloads

Three data sources; nothing else is sent or received. No telemetry.

| What | Where | When | How to disable |
|---|---|---|---|
| XML schema | GitHub (raw content) | On server start; changed files only (ETag caching) | `aet-eaw-edit.lsp.schema.source` = `local` |
| Game baseline | Configured URL (default: GitHub releases) | Once; cached in `%USERPROFILE%\.pg-swg-lsp\baselines\`; refreshed on new versions | `aet-eaw-edit.lsp.source.baseline.type` = `local` or `none` |
| Shader sources | Petroglyph's published download, or `aet-eaw-edit.shaders.sourceUrl` | Only on **Set Up Base Shader Sources** | Do not run the command; set `aet-eaw-edit.shaders.directory` to your own copy |

---

## Troubleshooting

**No diagnostics.** `aet-eaw-edit.lsp.enabled` must be `true` and `aet-eaw-edit.lsp.executable` must point to `PG.StarWarsGame.LSP.Server.exe`; then **EaWEdit: Restart LSP Server**.

**"LSP server failed to start".** Windows may have blocked the downloaded executable: Properties > **Unblock** on `PG.StarWarsGame.LSP.Server.exe`, then restart the server. Otherwise **Show Output** in the notification.

**Version mismatch.** Server and extension must match; download the matching server from the [releases page](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/releases).

**Features work in some files only.** Only directories listed in the `.pgproj` are indexed; add the paths to `directories.xml` or `directories.scripts`.

**"Found multiple .pgproj files under ...".** One `.pgproj` per workspace root: remove the extras or open the subfolder, then restart the server.

**"'directories.text'/'directories.textResourceType' were removed".** Pre-0.2.0 `.pgproj`; see [Upgrading from 0.1.x](#upgrading-from-01x).

**Localisation views missing.** `aet-eaw-edit.features.tools.localisation` = `true`; the server restarts on the change.

**A command or code action is missing.** Its feature flag is off; see the Flag column of the [commands table](#commands).

**"The Lua debugger is disabled".** `aet-eaw-edit.features.lua.debugger` = `true`. The debug type is always registered so `launch.json` validates; sessions start only with the flag on.

**"The game did not answer on 127.0.0.1:1234 within 60 s".** Debug build with `luadebug` run in its console required; retail builds never answer. A second game instance listens on 1235: set `aet-eaw-edit.game.luaDebugPort` or `port`. Remote machine: allow UDP to that port.

**Breakpoint: "The file is under none of the configured script roots".** The file is outside every project layer's script directories; move it, or add its root to `sourceRoots`.

**Launch refused: layer not runnable.** Declared directories under `Data/` and no space in the path, or pass the folders via `modPaths`. See [Lua debugger](#lua-debugger).

**Raw server output.** `aet-eaw-edit.lsp.debug.traceServer` = `messages`; **EaWEdit** output channel.

---

## Issues

[AlamoEngine-Tools/pg-starwarsgame-lsp/issues](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues)
