# Alamo Engine Tools - Empire at War Edit

A Visual Studio Code extension that adds editor intelligence for **Star Wars: Empire at War** and **Forces of Corruption** mod development.

> **Preview release** - This is early-access software. Not all planned features are complete, and behavior may change between versions. Windows x64 only for binary releases. Report issues at [AlamoEngine-Tools/pg-starwarsgame-lsp/issues](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues).

---

## Prerequisites

- **Visual Studio Code** 1.107 or later
- **Windows 11 (x64)** for the pre-built server binary - Windows 10 reached End of Life in October 2025 and is not supported
- **.NET 10 SDK** ([download](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)) - only needed if building from source; the released `.exe` is self-contained

---

## Installation

### 1. Download the release

From the [Releases](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/releases) page, download:

- `aet-eaw-edit-x.x.x.vsix` - the VS Code extension
- `PG.StarWarsGame.LSP.Server-x.x.x.zip` - the language server

### 2. Install the extension

Open the Command Palette (`Ctrl+Shift+P`), run **Extensions: Install from VSIX…**, and select the downloaded `.vsix`.

Or from a terminal:
```
code --install-extension aet-eaw-edit-x.x.x.vsix
```

### 3. Extract the language server

Unzip the server archive to a permanent location, for example `C:\tools\aet-lsp\`.

### 4. Point the extension at the server

Open VS Code settings (`Ctrl+,`) and set:

| Setting | Value |
|---|---|
| `aet-eaw-edit.lsp.executable` | Full path to `PG.StarWarsGame.LSP.Server.exe` (inside the extracted server archive) |
| `aet-eaw-edit.lsp.enabled` | `true` |

The extension activates automatically when you open an XML or Lua file, or when a workspace contains a `.pgproj` file.

---

## Project file (`.pgproj`)

The extension uses a small JSON file to understand your mod's layout. Place it at the root of your mod workspace and VS Code will pick it up automatically.

```json
{
  "name": "My Mod",
  "directories": {
    "xml":     ["data/xml"],
    "scripts": ["data/scripts"],
    "art":     ["data/art"],
    "audio":   ["data/audio"],
    "text":    ["data/text"]
  },
  "projectReferences": []
}
```

All paths are relative to the `.pgproj` file. `projectReferences` can list other `.pgproj` files whose symbols your mod inherits (e.g. the vanilla game data alongside your mod).

---

## XML features

The extension understands the full EaW/FoC XML data format - every game object type, tag, enum value, and cross-file reference.

- **Completions** - tag values auto-complete from enum definitions and named objects declared anywhere in the workspace
- **Hover** - hover over a tag or value to see its description and expected type
- **Diagnostics** - errors and warnings for unknown references, type mismatches, duplicate declarations, bad value formats, and deprecated fields
- **Go to definition** - `F12` or `Ctrl+Click` on any object reference jumps to its declaration, even across files
- **Find all references** - `Shift+F12` on a symbol lists every place it is used across the workspace
- **Rename** - `F2` on a symbol renames it consistently in every XML file in the workspace
- **Code actions** - quick-fix lightbulbs for common problems, including creating a missing localisation key directly from the editor
- **Code lens** - inline reference counts above every named object

---

## Lua features

Lua script files inside the declared `scripts` directories are indexed and checked for diagnostics.

---

## Localisation editor

An activity bar panel (the **AET Localisation** icon) provides a Key × Language table editor for `.csv`, `.xml`, and `.properties` localisation files.

- **Project picker** - switch between localisation files declared in the `.pgproj`
- **Inline editing** - edit translation values directly in the grid; changes are written back to disk immediately
- **Add Language** - adds a language column from the game's official language list; only languages not already present in the file are offered
- **Inherited baseline** - the "Inherited" toggle overlays all base-game EaW + FoC keys as read-only rows for reference
- **Search** - filters visible rows by key name or any translation value
- **Sortable columns** - click any column header to sort ascending, click again for descending, click a third time to restore original order
- **Initialise from baseline** - the **+ New** button creates a fresh localisation file pre-populated with all EaW + FoC baseline keys; choose CSV, XML, or NLS (Properties) format

---

## Commands

Available from the Command Palette (`Ctrl+Shift+P`):

| Command | Description |
|---|---|
| **AET: New Mod Project** | Creates a new `.pgproj` and initial directory structure |
| **AET: Reload Mod Project** | Re-reads the `.pgproj` and re-indexes the workspace |
| **AET: Re-validate Workspace** | Re-runs all diagnostics across indexed files |
| **AET: Restart LSP Server** | Stops and restarts the language server |
| **AET: Initialise Localisation Project from Baseline** | Creates a starter localisation file from the game baseline |

---

## Settings reference

### Language server

| Setting | Default | Description |
|---|---|---|
| `aet-eaw-edit.lsp.enabled` | `false` | Enable the language server |
| `aet-eaw-edit.lsp.executable` | _(empty)_ | Path to `PG.StarWarsGame.LSP.Server.dll` |
| `aet-eaw-edit.lsp.locale` | `en` | Language for server messages (`en`, `de`, `fr`, `es`, `it`, `pl`, `ru`) |

### Schema

The schema describes the complete EaW/FoC XML format and is downloaded automatically over HTTP by default. You can point it at a local copy instead if you are offline or prefer version-pinned schema files.

| Setting | Default | Description |
|---|---|---|
| `aet-eaw-edit.lsp.schema.source` | `http` | `http` to fetch automatically, `local` to use a local directory |
| `aet-eaw-edit.lsp.schema.localPath` | _(empty)_ | Path to a local `schema/eaw/` directory (when source is `local`) |

### Baseline

The baseline is a snapshot of all vanilla game objects and localisation keys. It powers reference validation and the localisation editor's "Inherited" toggle.

| Setting | Default | Description |
|---|---|---|
| `aet-eaw-edit.lsp.source.baseline.type` | `http` | `http` to download automatically, `local` for a local file, `none` to disable |
| `aet-eaw-edit.lsp.source.baseline.localPath` | _(empty)_ | Path to a local `.msgpack` baseline file (when type is `local`) |

### Localisation editor

| Setting | Default | Description |
|---|---|---|
| `aet-eaw-edit.localisation.editorEnabled` | `false` | Show the localisation editor panel in the activity bar |
| `aet-eaw-edit.localisation.format` | `format-dat` | Default format when creating a new localisation project (`format-dat`, `format-csv`, `format-xml`) |

---

## What this extension downloads

The extension contacts external servers only for these two data sources. Nothing else is sent or received.

| What | Where | When | How to disable |
|---|---|---|---|
| XML schema | GitHub (raw content) | On each server start; only changed files are re-fetched (ETag caching) | Set `aet-eaw-edit.lsp.schema.source` to `local` |
| Game baseline | Configured URL (default: GitHub releases) | Once on first use; cached in `%USERPROFILE%\.pg-swg-lsp\baselines\`; only re-downloaded when a new version is available | Set `aet-eaw-edit.lsp.source.baseline.type` to `local` or `none` |

No usage data, telemetry, or crash reports are sent.

---

## Troubleshooting

**No diagnostics appear**
Confirm `aet-eaw-edit.lsp.enabled` is `true` and `aet-eaw-edit.lsp.executable` points to `PG.StarWarsGame.LSP.Server.exe`. Use **EaWEdit: Restart LSP Server** after changing settings.

**Version mismatch warning on startup**
The server binary and extension must be the same version. Download the matching server from the [releases page](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/releases).

**Features only work in some files**
Only files inside directories declared in your `.pgproj` are indexed. Add the relevant paths to `directories.xml` or `directories.scripts` as needed.

**The localisation panel is not visible**
Set `aet-eaw-edit.localisation.editorEnabled` to `true`, then reload the window (`Ctrl+Shift+P` → **Developer: Reload Window**).

**Viewing server output**
Set `aet-eaw-edit.lsp.debug.traceServer` to `messages` and open the **EaWEdit** output channel in the Output panel.

---

## Issues

Report bugs and feature requests at [AlamoEngine-Tools/pg-starwarsgame-lsp/issues](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues).
