# Alamo Engine Tools - Empire at War Edit

Editor support for **Star Wars: Empire at War** and **Forces of Corruption** mod development.

This extension gives VS Code a full understanding of the game's XML data format and Lua scripting layer: completions drawn from the live object graph, hover documentation for every tag and enum value, cross-file diagnostics, go-to-definition, find-all-references, rename, and more.

> **Preview release** - This is early-access software. Not all planned features are complete, and behavior may change between versions. Please report anything unexpected on the [issue tracker](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues).

---

## Requirements

- Visual Studio Code 1.107 or later
- Windows 11 (x64)
- The PG.StarWarsGame.LSP server binary (see Getting started below)

No .NET runtime installation is required. The server ships as a self-contained Windows executable.

> **Windows 10 is not supported.** Microsoft ended mainstream support for Windows 10 in October 2025. The extension may work on Windows 10 but no issues specific to it will be investigated or fixed.

---

## Getting started

### 1. Download the server

Go to the [Releases](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/releases) page and download both:

- `aet-eaw-edit-x.x.x.vsix` - the VS Code extension (if installing manually instead of through the marketplace)
- `PG.StarWarsGame.LSP.Server-x.x.x-win-x64.zip` - the language server

Extract the server archive to a permanent folder, for example `C:\tools\aet-lsp\`.

### 2. Configure the extension

Open VS Code settings (`Ctrl+,`) and set:

| Setting | Value |
|---|---|
| `aet-eaw-edit.lsp.executable` | Full path to `PG.StarWarsGame.LSP.Server.exe` inside the extracted folder |
| `aet-eaw-edit.lsp.enabled` | `true` |

The extension activates when you open an XML or Lua file, or when the workspace contains a `.pgproj` file. If the server binary is missing or its version does not match what this extension expects, a notification appears with a link to the correct release.

### 3. Create a mod project file

Place a `.pgproj` file at the root of your mod workspace so the extension knows which directories to index:

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

All paths are relative to the `.pgproj` file. The `projectReferences` field can list other `.pgproj` files whose symbols your mod inherits - for example, a base game `.pgproj` placed alongside your mod.

---

## XML features

The extension indexes the full EaW/FoC object graph across your workspace and uses it to drive every editor feature.

- **Completions** - tag values auto-complete from enum definitions and named objects declared anywhere in the workspace
- **Hover** - hover over any tag name, enum value, or object reference to see its type, description, and valid values
- **Diagnostics** - errors and warnings for unknown references, type mismatches, duplicate declarations, malformed values, and deprecated fields
- **Go to definition** - `F12` or `Ctrl+Click` on any object reference jumps to its declaration, even across files
- **Find all references** - `Shift+F12` on a symbol lists every file and line where it is used
- **Rename** - `F2` on a symbol renames it consistently across every XML file in the workspace
- **Code actions** - quick-fix lightbulbs for common problems, including creating a missing localisation key from within the editor
- **Code lens** - inline reference counts shown above every named object
- **Variant inheritance** - the Show Effective Object command opens a read-only view of the fully merged XML for any `Variant_Of_Existing_Type` object

---

## Lua features

Lua script files inside the declared `scripts` directories are indexed and checked for diagnostics.

---

## Localisation editor

An activity bar panel provides a key-by-language table editor for `.csv`, `.xml`, and `.properties` localisation files.

- **Project picker** - switch between localisation files declared in the `.pgproj`
- **Inline editing** - edit translation values directly in the grid; changes are saved to disk immediately
- **Add Language** - adds a language column drawn from the game's official language list
- **Inherited baseline** - the Inherited toggle overlays all base-game EaW and FoC keys as read-only rows for reference
- **Search** - filter visible rows by key name or any translation value
- **Sortable columns** - click a column header to sort ascending, click again for descending, click a third time to restore the original order
- **Initialise from baseline** - creates a fresh localisation file pre-populated with all EaW and FoC keys, in CSV, XML, or Properties format

---

## Commands

Available from the Command Palette (`Ctrl+Shift+P`):

| Command | Description |
|---|---|
| EaWEdit: New Mod Project | Creates a new `.pgproj` and initial directory structure |
| EaWEdit: Reload Mod Project | Re-reads the `.pgproj` and re-indexes the workspace |
| EaWEdit: Re-validate Workspace | Re-runs all diagnostics across indexed files |
| EaWEdit: Restart LSP Server | Stops and restarts the language server |
| EaWEdit: Initialise Localisation Project from Baseline | Creates a starter localisation file from the game baseline |
| EaWEdit: Show Effective Object (Variant Inheritance) | Opens a read-only view of the fully resolved XML for a variant object |

---

## Settings reference

### Language server

| Setting | Default | Description |
|---|---|---|
| `aet-eaw-edit.lsp.enabled` | `false` | Enable the language server |
| `aet-eaw-edit.lsp.executable` | _(empty)_ | Path to `PG.StarWarsGame.LSP.Server.exe` |
| `aet-eaw-edit.lsp.locale` | `en` | Language for hover text and diagnostics (`en`, `de`, `fr`, `es`, `it`, `pl`, `ru`) |

### Schema

The schema describes the complete EaW/FoC XML format. It is fetched from GitHub when the server starts and cached between restarts using HTTP ETags, so only changed files are re-downloaded. You can switch to a local copy via the settings below if you prefer to work offline or pin a specific version.

| Setting | Default | Description |
|---|---|---|
| `aet-eaw-edit.lsp.schema.source` | `http` | `http` to fetch from GitHub; `local` to use a local directory |
| `aet-eaw-edit.lsp.schema.localPath` | _(empty)_ | Path to a local `schema/eaw/` directory (only when source is `local`) |

### Baseline

The baseline is a pre-built snapshot of all vanilla EaW and FoC game objects and localisation keys. It is downloaded once and stored in `%USERPROFILE%\.pg-swg-lsp\baselines\`. The cached file is only re-downloaded when a new version is available. The baseline powers cross-file reference validation and the Inherited toggle in the localisation editor.

| Setting | Default | Description |
|---|---|---|
| `aet-eaw-edit.lsp.source.baseline.type` | `http` | `http` to download automatically; `local` for a local file; `none` to disable entirely |
| `aet-eaw-edit.lsp.source.baseline.localPath` | _(empty)_ | Path to a local baseline file (only when type is `local`) |

### Localisation editor

| Setting | Default | Description |
|---|---|---|
| `aet-eaw-edit.localisation.editorEnabled` | `false` | Show the localisation editor panel in the activity bar |
| `aet-eaw-edit.localisation.format` | `format-dat` | Default format for new localisation projects (`format-dat`, `format-csv`, `format-xml`) |

---

## What this extension downloads

This extension contacts external servers only for the two data sources described above. Nothing else is sent or received.

| What | Where | When | How to disable |
|---|---|---|---|
| XML schema | GitHub (raw content) | On each server start; individual files only re-fetched when changed (ETag caching) | Set `aet-eaw-edit.lsp.schema.source` to `local` |
| Game baseline | Configured URL (default: GitHub releases) | Once on first use; cached in `%USERPROFILE%\.pg-swg-lsp\baselines\`; only refreshed when a new version is available | Set `aet-eaw-edit.lsp.source.baseline.type` to `local` or `none` |

No usage data, telemetry, or crash reports are sent.

---

## Troubleshooting

**No diagnostics appear**
Confirm `aet-eaw-edit.lsp.enabled` is `true` and `aet-eaw-edit.lsp.executable` points to `PG.StarWarsGame.LSP.Server.exe`. Run EaWEdit: Restart LSP Server after changing settings.

**"LSP server failed to start" or status bar shows an error**
Windows may have blocked the server executable because it was downloaded from the internet. Right-click `PG.StarWarsGame.LSP.Server.exe` in Explorer, choose Properties, tick **Unblock** at the bottom of the General tab, then click OK. After unblocking, restart the server with EaWEdit: Restart LSP Server. If it still fails, click **Show Output** in the error notification to see the full error in the EaWEdit output channel.

**Version mismatch warning**
The server binary and extension must be the same version. Download the matching server from the [releases page](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/releases).

**Features only work in some files**
Only files inside directories listed in your `.pgproj` are indexed. Add the relevant paths to `directories.xml` or `directories.scripts`.

**The localisation panel is not visible**
Set `aet-eaw-edit.localisation.editorEnabled` to `true`, then reload the window (`Ctrl+Shift+P` > Developer: Reload Window).

**Viewing raw server output**
Set `aet-eaw-edit.lsp.debug.traceServer` to `messages` and open the **EaWEdit** output channel in the Output panel.

---

## Issues

Report bugs and feature requests at [AlamoEngine-Tools/pg-starwarsgame-lsp/issues](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues).
