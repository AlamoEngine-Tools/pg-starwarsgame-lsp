# Changelog

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
