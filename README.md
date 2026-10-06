# Alamo Engine Tools - Empire at War Edit

Visual Studio Code extension and language server for **Star Wars: Empire at War** and **Forces of Corruption** mod development.

- XML and Lua intelligence
- Story tooling
- Localisation editor
- Model preview
- Lua debugger

> **Preview release.** Not every feature is complete; behaviour may change between versions. Binary releases are Windows x64 only. Issues: [AlamoEngine-Tools/pg-starwarsgame-lsp/issues](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues).

---

## Prerequisites

- **Visual Studio Code** 1.107 or later
- **Windows 11 (x64)** for the pre-built server; Windows 10 is unsupported (end of life October 2025)
- **.NET 10 SDK** ([download](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)) only for building from source; the released `.exe` is self-contained

---

## Installation

### 1. Download the release

From [Releases](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/releases):

- `aet-eaw-edit-x.x.x.vsix` - the extension
- `PG.StarWarsGame.LSP.Server-x.x.x.zip` - the language server

### 2. Install the extension

Command Palette (`Ctrl+Shift+P`) > **Extensions: Install from VSIX...**, or:

```
code --install-extension aet-eaw-edit-x.x.x.vsix
```

### 3. Extract the language server

Unzip to a permanent location, e.g. `C:\tools\aet-lsp\`.

### 4. Point the extension at the server

| Setting | Value |
|---|---|
| `aet-eaw-edit.lsp.executable` | Full path to `PG.StarWarsGame.LSP.Server.exe` |
| `aet-eaw-edit.lsp.enabled` | `true` |

Activation: an open XML or Lua file, or a `.pgproj` in the workspace.

---

## Project file (`.pgproj`)

At the root of the mod workspace:

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

- `_type` and `_typeVersion` - the file's format, written and maintained by the extension. A file without them loads as format 1.0.0; a newer format is refused.
- Paths are relative to the `.pgproj`.
- `projectReferences` - other `.pgproj` files whose symbols the mod inherits, e.g. the vanilla game data.
- `localisation.type` - `CSV`, `DAT`, `XML` or `NLS`.
- `icons` (optional) - the project's own command-bar mega texture and loose icon sources.

The extension's README documents every node.

---

## XML features

- **Completions** - enum values and named objects from the whole workspace
- **Hover** - description, type and valid values
- **Diagnostics**
  - Unknown references and tags
  - Unnamed objects, missing required tags
  - Type mismatches, bad value formats, deprecated fields
  - Duplicate declarations
- **Engine rules** - about 150 checks taken from the game's parser and error messages
- **Navigation**
  - Go to definition (`F12`)
  - Find all references (`Shift+F12`)
  - Rename (`F2`)
  - Reference-count code lenses
- **Code actions** - quick fixes, including creating a missing localisation key
- **Variant inheritance** - **Show Effective Object** opens the merged result of a `Variant_Of_Existing_Type` object, each tag annotated with its source

---

## Lua features

Scripts under the declared `scripts` directories are indexed.

- Completion, navigation, rename
- Code lenses, inlay hints, quick fixes
- Hover and diagnostics _(work in progress)_
- Engine surface from measured stub files: registered functions, wrapper classes, the standard library the game
  opens, per-host globals; string arguments naming game objects, text keys and enum values complete and resolve

---

## Lua debugger

> **Work in progress, off by default.** Flag: `aet-eaw-edit.features.lua.debugger`. Requires a debug build of the game with its Lua debug server running; retail builds have none.

> **With thanks to EvilBobTheBob** ([@andrewfullard](https://github.com/andrewfullard)), of Phoenix Rising and EaWX, whose [`eaw-lua-debugger`](https://github.com/andrewfullard/eaw-lua-debugger) documented the game's Lua debug server protocol. This debugger was built against that description; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Debug type **Empire at War Lua**:

- Breakpoints in project scripts
- Attach to a running game, or launch it with the mod chain
- Call stack, locals, watches, a Lua Debug Console
- **Lua Scripts** view: every running script instance with its coroutine threads, to break in (the game cannot start a script on request)

`F5` in a Lua file attaches; `launch.json`:

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

Launch passes one `MODPATH=` per project layer, mod first. Each layer needs its declared directories under `Data/` and no space in its path, or the launch is refused; `modPaths` overrides the chain.

Game limits, reported rather than hidden:

- No breakpoint conditions
- Whole game frozen while a script is stopped
- Pause at the next Lua line
- Table expansion only with `aet-eaw-edit.game.unsafeTableExpansion`

The extension's README documents every attribute.

---

## Story mode

Campaigns are followed from `CampaignFiles.xml` through plot manifests to the `Story_*.xml` thread files. Opt-in and in development; see the story flags in the extension's README.

- **Campaign Editor** view
- Story graph
  - AND/OR junctions, portals, tactical plots
  - Lifecycle colours and a colour key
  - Filters: name, branch, lifecycle, an event's chain
  - Responsive at 1000+ events
- Edit mode: in-place editing, written as minimal edits
- Campaign diagnostics
- Story symbols indexed across XML and Lua: navigation, rename

---

## Model preview

- **Alamo Model Preview** editor for `.alo` and `.ala` files
- **Preview Assembled Unit** builds a GameObject as the game does: hardpoints on attachment bones, turrets, firing arcs, team colour
- Lenses
  - Model: tree, LOD and ALT, skeleton, cameras, inspector
  - Animation: clip library, loop rule, transport
  - Gameplay: attacker weapons and abilities, hardpoint destroy and repair, damage log with armour types
- Particle systems, saved shots, handoff to AloViewer and the Particle Editor
- **Set Up Base Shader Sources** fetches Petroglyph's published shader sources; never redistributed
- Game models need `aet-eaw-edit.lsp.source.baseGameDirectory` and `expansionDirectory`
- No sounds

---

## Encyclopedia preview

**Preview Encyclopedia Popup** (command and code lens) draws the in-game tooltip card.

- Icon, name, class line, `Encyclopedia_Text` wrapped as the game wraps it, faction switch
- Game language from `aet-eaw-edit.lsp.localisation.language`
- Icons from the mega texture: game icons baked into the baseline; a project's own `mt_commandbar` (`.pgproj` `icons`) replaces them

---

## Suppressing diagnostics

Every diagnostic carries a stable id `aetswg-<group>-<number>`, shown in the Problems panel.

### Quick fixes

`Ctrl+.` on a problem offers four scopes, none preselected:

- **Suppress ... for this line**
- **Suppress ... for this `<Unit>` / function / chapter**
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

A scope with no target covers its own line only. Project-wide suppressions live in `.aetswg/suppressions.json`; commit it.

### Lists, wildcards, and reasons

```xml
<!-- aetswg:suppress aetswg-010-0002, aetswg-010-0003 reason:: deliberate shadow, cleared with the art team -->
```

`*` in place of the number silences a group:

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

### When a directive is wrong

- `aetswg-013-0001` - an entry that is not an id or wildcard (e.g. `aetswg-4-1` for `aetswg-004-0001`)
- `aetswg-013-0002` - a directive naming no diagnostic

Only the bad entry is rejected.

### `<Override>` is not a suppression

`<!-- <Override Name="..."/> -->` declares intended shadowing of a lower layer, a claim the server checks.

---

## Localisation editor

**Localisation Editor** view with **Text files** and **Credits files**; one grid tab per file.

- Staged edits with explicit **Save**; refused if the file changed on disk
- **Validate** without writing
- Rows via right-click menu
- Add and fill languages
- Inherited rows hidden by default
- Text, wildcard and regex filters; column picker
- **Export to DAT**, **Convert to another format**
- New projects from the baseline or by import
- Saves rewrite changed rows only

### Credits files

- Ordered, position-addressed rows; duplicates, `[TBL]` spacers and order survive editing
- Tile library of the three line kinds
- Preview crawl as in the game

Detection by engine naming (`credits*`) or:

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

One format per project. `"type": "DAT"` projects edit compiled `.dat` files directly; other formats export to `.dat`.

---

## Commands and settings

The extension contributes 29 commands and 70 settings. Both are documented, with defaults and the
feature flag each belongs to, in the extension's own README:

- [Commands](PG.StarWarsGame.LSP.Client.VSCode/aet-eaw-edit/README.md#commands)
- [Settings reference](PG.StarWarsGame.LSP.Client.VSCode/aet-eaw-edit/README.md#settings-reference)
- [Feature flags](PG.StarWarsGame.LSP.Client.VSCode/aet-eaw-edit/README.md#feature-flags)

That file ships with the extension, so it is the copy that stays in step with what is installed.

---

## What this extension downloads

Two data sources plus the on-demand shader sources; nothing else is sent or received. No telemetry.

| What | Where | When | How to disable |
|---|---|---|---|
| XML schema | GitHub - newest compatible schema release | On server start; nothing when the release is already cached; default branch when no release exists | `aet-eaw-edit.lsp.schema.source` = `local` |
| Game baseline | Configured URL (default: GitHub releases) | Once; cached in `%USERPROFILE%\.pg-swg-lsp\baselines\` | `aet-eaw-edit.lsp.source.baseline.type` = `local` or `none` |
| Shader sources | Petroglyph's published download, or `aet-eaw-edit.shaders.sourceUrl` | Only on **Set Up Base Shader Sources** | Do not run the command |

---

## Troubleshooting

**No diagnostics.** `aet-eaw-edit.lsp.enabled` = `true` and `aet-eaw-edit.lsp.executable` set; then **EaWEdit: Restart LSP Server**.

**Version mismatch on startup.** Server and extension must match; download the matching server from the [releases page](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/releases).

**Features work in some files only.** Only directories declared in the `.pgproj` are indexed.

**Localisation views missing.** `aet-eaw-edit.features.tools.localisation` = `true`; the server restarts on the change.

**"The game did not answer on 127.0.0.1:1234 within 60 s".** Debug build with `luadebug` run in its console required; match `aet-eaw-edit.game.luaDebugPort` to the instance (first free port from 1234 upward).

**Server output.** `aet-eaw-edit.lsp.debug.traceServer` = `messages`; **EaWEdit** output channel.

---

## Building from source

**git, the .NET 10 SDK and Node.js.** The server builds against sibling repositories listed in `workspace.deps`; the setup script clones them next to this repo.

```
git clone https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp.git PG.StarWarsGame.LSP
cd PG.StarWarsGame.LSP
./setup-workspace.sh            # or: pwsh ./setup-workspace.ps1
dotnet build PG.StarWarsGame.LSP.slnx
```

- **Back in sync** - the same command: fast-forwards this repo and the owned repos (`schema/`, `baseline/`) on their default branch, moves ModVerify and PetroglyphTools to their pinned commits
  - A repo with uncommitted changes or on another branch is left alone and reported
- **State without changes** - `./setup-workspace.sh check`
- **E2E data** - `--groups build,e2e` adds `baseline/`; the game files in `eaw/` and `foc/` are not distributable
- **Cross-repo changes** - a schema or baseline branch named like the LSP branch; CI on the pull request builds against it
- **External update** - the wanted commit checked out in `../ModVerify` or `../PetroglyphTools`, then `./setup-workspace.sh record` and a commit of `workspace.deps`

### Releasing

**GitHub Actions, two runs.** Maintainers only.

1. **Prepare Release** - Actions tab, input `version`; branch `release/v<version>` with the version bump, and its pull request
2. **Changelog** - the `## <version>` section of the extension's `CHANGELOG.md`, committed to that branch; the push runs CI
3. **Merge** the pull request into `master`
4. **Release** - Actions tab, on `master`; tests and builds, then waits
5. **Approve** - one approval in the `release` environment publishes everything: GitHub release with the changelog section as notes, tag, Marketplace
   - All or nothing: a draft release first, the Marketplace next, the draft made public last; a failure removes the draft

- **Schema first** - the schema version the server is built against must already be released; eaw-schema releases the same way, Prepare Release then Release, and nothing publishes on a merge
- **Nothing automatic** - no step starts the next; the release branch reaches `master` only through the merge

---

## Issues

[AlamoEngine-Tools/pg-starwarsgame-lsp/issues](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues)

---

## Acknowledgements

**EvilBobTheBob** ([@andrewfullard](https://github.com/andrewfullard)), of Phoenix Rising and EaWX, for [`eaw-lua-debugger`](https://github.com/andrewfullard/eaw-lua-debugger) - the Python client that documented the game's Lua debug server protocol. The [Lua debugger](#lua-debugger) here was built against that description.

---

## Licensing and third-party components

[MIT License](LICENSE). Third-party components and prior work: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Every release ships an SPDX SBOM per artifact (language server, extension) attached to the GitHub release.

No game assets are redistributed; the editor reads game data from your own installation.
