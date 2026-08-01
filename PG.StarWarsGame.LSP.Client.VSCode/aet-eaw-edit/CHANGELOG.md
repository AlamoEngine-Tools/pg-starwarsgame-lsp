# Changelog

## Unreleased

### Features

- Diagnostics can be suppressed, at the scope you choose, in every language the extension checks. Not every reported problem is one you want fixed - a deliberate duplicate, an asset generated at build time, a reference the extension cannot see - and until now the only options were to live with the warning or turn a whole feature off. Every diagnostic can now be silenced individually by writing a comment next to it, with four scopes to pick from: the next item, the enclosing declaration, the whole file, or the entire project. The same directive works in XML (`<!-- aetswg:suppress aetswg-004-0001 -->`), in Lua (`-- aetswg:suppress ...`) and in dialog scripts (`# aetswg:suppress ...`); the enclosing declaration means the object, the function or the `[CHAPTER n]` section respectively. One comment can name several diagnostics at once, separated by commas, and can carry a note for whoever reads it next after `reason::`. A quick fix on the diagnostic writes it for you, offering the narrowest scope first; the first three insert a comment you can see and undo like any other edit, and the project-wide one is recorded in `.aetswg/suppressions.json`, which is meant to be committed so your team shares the decision. In dialog scripts the quick fixes are opt-in via `aet-eaw-edit.features.dialog.codeActions`, in line with the rest of the dialog language service; the directives themselves are always honoured. The whole syntax is documented under "Suppressing diagnostics" in the README. See [#66](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/66), [#67](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/67) and [#69](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/69).

- A suppression comment that would not work now says so. Identifiers are numeric by design, which makes them easy to mistype - and a directive naming something the extension cannot read silences nothing, so the diagnostic simply stayed where it was with no hint that the comment beside it was the problem. A misspelled entry is now reported on the comment itself (`aetswg-013-0001`), naming the entry it could not read, as is a directive that names no diagnostic at all (`aetswg-013-0002`). The most common slip is dropping the padding zeros - `aetswg-4-1` for `aetswg-004-0001` - which now gets an explanation instead of silence. Only the bad entry is rejected: in a comma-separated list the identifiers you got right still take effect. These warnings can themselves be silenced with `aetswg-013-*`.

- Every diagnostic now carries a stable identifier, shown next to the message - `aetswg-004-0001` and the like, where the middle number is the family it belongs to (references, enum values, asset files, story, syntax and so on). Families describe the kind of problem, not the language it was found in, so silencing `aetswg-001-*` stops unresolved-reference reports in XML and Lua alike. The identifier is what a suppression names, so it is deliberately narrow: where one check previously reported several different problems under one banner, each now has its own identifier and can be silenced without silencing the others. Lua parse errors keep the number Loretta gives them, so `LUA1003` is reported as `aetswg-012-1003` and can still be looked up. A whole family can be turned off at once with `aetswg-004-*` when that is genuinely what you want - for example to stop checking whether asset files exist in a workspace where they are produced by a build step. See [#68](https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp/issues/68).

- **Breaking:** the older `<!-- lsp:suppress duplicate-symbol -->` comment is no longer honoured. It predates diagnostic identifiers and had no way to name anything but that one check. Replace each occurrence with `<!-- aetswg:suppress aetswg-010-0001 -->`; a plain find-and-replace does it, and the new form is if anything more accurate, since it covers exactly the element that follows rather than a fixed five lines. Files still carrying the old comment will report duplicate symbols again until it is updated.

- The `<!-- <Override Name="..."/> -->` marker is unchanged, and is **not** a suppression. It declares that shadowing a definition from a lower layer is deliberate - an assertion about your mod, in the spirit of Java's `@Override` - rather than a request to hide a message. The two are now kept clearly apart, and the shadow warnings say "declare it" instead of "suppress it" to match.

- Suppressing a diagnostic project-wide now takes effect immediately in every language, and an unusable `suppressions.json` says so. **Suppress ... across the project** wrote the entry but only refreshed XML, so using it from a Lua or dialog file left the diagnostic on screen until that file was next edited - it looked as though the suppression had not worked. All three languages are now refreshed together. In the same spirit, an entry in `.aetswg/suppressions.json` that is not a diagnostic id, or a file too damaged to read at all, is reported in the editor instead of only in the server log; that file is hand-editable and committed, so a typo in it was previously invisible.

- The localisation editor has moved out of the sidebar and into a proper editor tab, one per file. A flyout was the wrong host for a wide table: it competed with the file explorer for width, only one file could be open at a time, and the grid could never sit beside the XML that references its keys. There is now an **EaWEdit: Localisation** activity bar view listing every localisation file the project declares, and opening one from it gives you a full editor tab that can be split, moved to another column, or kept open beside anything else. Two files can be compared side by side.

- Localisation files are grouped into **Text files** and **Credits files**, and credits files are understood as what they are. A credits file is ordered and allows the same key more than once - it is a running list, not a lookup table - so it could never be edited safely by a key-addressed grid, which would collapse repeated keys the moment you touched one. Rows are now addressed by position throughout, so duplicate keys, blank spacer rows and row order all survive editing, and the grid offers insert, delete and reorder on them. Files are recognised by the engine's own naming (anything beginning with `credits`); a project that names things differently can say so in its `.pgproj`:

  ```jsonc
  "localisation": {
    "type": "CSV",
    "directory": "data/text",
    "credits": { "detection": "convention", "files": ["rolls.csv"] }
  }
  ```

  `detection` is `convention` (the default), `explicit` (only the files listed) or `none` (nothing is a credits file - the opt-out for a project whose text file merely looks like one).

- **Edits are staged and written by an explicit Save.** Previously every cell wrote to disk the moment you left it, so a mistyped value was already saved before you noticed. Changes now accumulate in the tab - shown as a count on the Save button - and reach the file only when you save, as a single all-or-nothing write. **Validate** checks a batch without writing, reporting duplicate or empty keys for text files and staying quiet about them for credits files, where duplicates and blanks are the format. If the file changed on disk since the tab loaded it, the save is refused with an offer to reload rather than overwriting someone else's work.

  Saving writes the file directly, so it does not create an undo entry in the editor and an open unsaved text editor on the same file can still conflict - the file-changed check turns that into a refusal rather than a silent overwrite.

- A save rewrites only the rows you changed. Every untouched row is written back exactly as it was read, keeping its original quoting, comments, blank lines and line endings, so editing one cell of a 19,000-row file produces a one-line diff instead of an unreviewable one.

- Credits files export to `CreditsText_LANGUAGE.dat`, which is what the engine loads for the crawl - the export previously wrote `MasterTextFile_*` for every file, so a credits file could not be exported into something the game would read. Credits exports are the file itself in order, without the game baseline merged underneath it.

- In a credits file the key column is understood as what it actually is: a formatting instruction, not an identifier. That is why the format repeats the same "key" hundreds of times. The shipped `creditstext_english.dat` uses two - `HEADER` for a label line (a role, or a section heading) and `CENTER` for the line beneath it - plus the value `[TBL]`, which is the engine's marker for a blank spacer line rather than any text. The grid now offers those directives as a dropdown instead of a free-text box, and a **Spacer** button inserts a `[TBL]` row, so a mistyped `CENTRE` or a stray trailing space cannot silently produce a row the engine ignores.

- Rows are edited from a right-click menu on the row itself, and what it offers depends on the kind of file. A credits file is a running list where position is the content, so it gets insert above, insert below, move up, move down and delete. A text file is a lookup table where position means nothing, so it gets just **Add translation...** and **Delete row** - there is no meaningful difference between adding above and below.

- **Translation files and credits files are now separate editors.** They are different kinds of thing - a translation file is a lookup table where the key identifies the entry and the row order carries no meaning, while a credits file is a running order the crawl plays top to bottom, repeating the same key on hundreds of rows because there it is a formatting instruction. One grid trying to be both is why credits behaviour kept turning up in text files. Each now has only the controls that make sense for it: no sorting or inherited filtering in credits, no spacers, directives or reordering in translations. Translation edits address entries by key throughout, so nothing in that editor depends on where a row happens to sit.

- **Adding a translation can start from what the game already defines.** Typing in the key field suggests keys that exist in the layers below but not in this file - which is exactly what an override is - and picking one fills in the inherited text for every language, ready to edit. Previously the only way to override a line was to go and read the game's own file for the exact key and type it in by hand. The suggestions come from the baseline already loaded for the Inherited toggle, so this costs no extra work when the dialog opens.

- The table has a footer saying what it is showing: the total row count, how many rows are being held back, and how many filters are doing the holding. Previously the dock reported "19222 of 19222 rows" - which says nothing when the two are equal, and does not read at a glance without separators - alongside the selected row and the kind of file, both of which the grid and the tab already show.

- The credits crawl moved to the editor's title bar, where Markdown and LaTeX editors put their previews, and behaves the way those do. **Open Credits Preview to the Side** opens the crawl in its own tab beside the editor and keeps it in step with what you have staged, so a reordered or inserted line plays without saving first. **Play Credits Crawl Full Screen** plays it over the editor instead, filling the screen where the host allows it; leaving the preview restores the window.

- The filter controls are the story graph editor's controls, not a lookalike. The search box and the scope picker had their own colours, padding and focus treatment, and the three search-mode toggles were filled buttons using a different accent from every other icon button in the extension. They now use the graph editor's rules verbatim: the same input and select styling with the same focus border, the same soft icon buttons, and the pickers stacked in the same full-width column beneath.

- The localisation dock is laid out like the story graph editor's: Save is an icon button pinned to the left of the header carrying its pending count, Validate is a soft pill pinned right that takes the colour of its own state, and the filters sit at the foot of the dock rather than the top. The **Add translation...** button is gone - it is on the right-click menu, including on the empty area of a file with no rows yet. **Open as text** has moved to the file's own right-click menu in the Files tree, beside Export to DAT, since opening the raw file is something you do to a file rather than from inside the editor showing it.

- Every glyph the story graph editor used as an icon is now a real icon. Buttons that read as `x`, `->`, `v`, a pencil, a bin, a tick and so on were literal characters standing in for icons, which do not follow the theme, render differently on every platform, and are not something a screen reader can make sense of. They are codicons now, matching the rest of the extension; the AND/OR shapes in the legend and the node palette are drawn rather than typed. Remaining stray characters in user-facing text - an ellipsis in four placeholders and the status bar, a middle dot in the localisation picker, arrows in two prompts - were replaced with plain ASCII.

- The credits crawl has a proper sky. It was six repeating gradient tiles, so the same handful of stars recurred on a fixed pitch across the whole screen; it is now a generated field of several hundred, varied in size and brightness and repeating nowhere.

- Validation results moved out of the dock and into a bar across the foot of the table, the same shape the story graph editor uses: draggable from its top edge, closeable, and reopening whenever a fresh result arrives. Clicking a result takes you to the entry it names - lifting the filter, and revealing inherited entries, if either was hiding it. The dock was the wrong home for them: it is narrow enough that every message was truncated, and a list appearing there shifted the controls above it.

- **Validate now describes the file, not just what you have staged.** It used to be greyed out and unpressable until an edit was made, showing a question mark that meant nothing - even though a file can arrive with problems already in it, such as a duplicate key it was saved with. The file is checked as soon as it opens, so the indicator shows a green tick or a red count from the start, and it is always pressable. Staging an edit returns it to a question mark, since the last verdict no longer describes what is there, and the tooltip says what it found rather than repeating the button's name.

- **A credits file is now built from a library of the three kinds of line it is made of** - a label, the name beneath it, and a blank line - shown as tiles in the dock. Drag one onto the table and a line marks exactly where it will land, above or below whichever row you are over; drop it and it goes there, ready to type into. Clicking a tile adds it at the end instead, so the library works without dragging. This says what the format actually is far better than a menu item and a directive dropdown did: the whole shipped credits file is nothing but these three.

- Row creation in a credits file is entirely on the right-click menu, including on the empty area below the rows - which is also how a file with no rows yet is reached. The **Add row at end** button is gone.

- The row menu no longer opens off the edge of the window. Right-clicking the last row of a full table put its lower half below the window where none of its items could be reached; it now flips to the other side of the pointer, and is clamped inside the window - with a scrollable height - when the window is too small for it to fit either way.

- Fixed a horizontal scrollbar under a table whose columns fitted perfectly well. Every control in the grid is sized to fill its column and also carries padding and a border, which without `box-sizing` made each one wider than the column holding it. The same fault had the dock's filter box hanging over its right-hand edge.

- Fixed the sorted column header being unreadable in light themes. It was tinted with the colour VS Code uses for text on a selected row, which assumes the dark background a selection has - on an ordinary header that came out white on near-white. The sorted column is now marked by its arrow and a heavier label instead.

- **Adding a translation asks for it up front rather than dropping an empty row into the grid.** A dialog takes the key and a field for every language the file declares, and will not create anything until the key is usable: it must not be empty, and it must not already exist in the file - checked without regard to case, which is how the server compares keys, so a clash cannot slip through and fail at Save instead. The key is trimmed, languages left blank stay blank, and the finished row is added at the end, scrolled to and focused. Any active filter or sort is cleared first, so the new row is where you can see it rather than wherever its key happens to sort.

- Blank lines in a credits file are shown as what they are. Rather than a row containing the literal text `[TBL]`, a spacer renders as a dimmed rule labelled "blank line", with no fields to type into - it can be placed and removed from the row menu, but there is nothing meaningful to edit inside one. This applies to credits files only: in an ordinary text file `[TBL]` is just a value, and a row holding it stays fully editable.

- The row menu no longer flickers away when the caret is in another row. Right-clicking a row while a cell elsewhere was being edited opened the menu and closed it again a frame later: blurring the cell made the browser scroll the grid back by a few pixels, and the menu was dismissing itself on any scroll at all. It now watches whether its own row has actually moved, so a real scroll still dismisses it and a blur does not.

- Rows that only repeat what a lower layer already says are hidden by default, as they were in the sidebar editor. A mod's text file is usually a copy of the game's with a handful of lines altered, so showing all of it buries the part that matters. The **Inherited** checkbox in the dock brings them back, with a count of how many there are; when shown they are dimmed and italic so it stays obvious which lines the file actually changes, and a row that has been overridden offers **Reset to inherited value** on its right-click menu, staged like any other edit rather than written immediately. This applies to text files only - a credits file keys every row by a formatting directive, so matching it against a baseline by key would pair unrelated lines.

- Text files can be sorted by clicking a column header - by key or by any language - cycling ascending, descending, then back to the order on disk. Sorting a language column puts the untranslated rows together, which is the quickest way to see what is still missing. It changes the view only: rows keep their identity underneath, so an edit made while sorted lands on the row you clicked. Credits files are deliberately not sortable, because there the running order is the content rather than a presentation choice. Adding a row clears any active sort, so the new row appears where you asked for it instead of jumping to wherever an empty key would sort.

- Credits files have a preview that plays them the way the game does: centred text scrolling up a starfield, label lines set small above the larger names beneath them, and blank lines as real pauses. It renders what you have staged rather than what is on disk, so a reordered or newly inserted line shows up before you save - which makes row order and spacing, the things this format is actually about, visible at a glance. It scrolls at a fixed reading pace, so a long file plays no slower than a short one and the first line appears immediately rather than after a wait. Pause, restart, speed and language are on the toolbar, `Esc` closes it, and it falls back to a static list if you have asked your system for reduced motion.

- **Removed:** the `aet-eaw-edit.localisation.editorEnabled` setting, which existed only to show or hide the old sidebar panel. The localisation views now follow `aet-eaw-edit.features.tools.localisation` alone. `aet-eaw-edit.localisation.format` is unchanged.

- Compiled `.dat` files are editable, not just an export target. The engine ships its text as `.dat`, so a project declaring `"type": "DAT"` can now open one in the grid, edit it and save it back. The language is taken from the filename (`creditstext_english.dat`), and the file's existing sort order is preserved on write - rewriting an unsorted credits file as a sorted one would reorder the crawl into checksum order. A `.dat` holds exactly one language, so adding a column is refused rather than half-applied.

- A note on scope: each project still reads one localisation format, taken from its `.pgproj`. A CSV project's credits file is expected to be a `.csv` alongside its text file, and mixing formats within one project remains out of scope. The format in `.pgproj` is now matched case-insensitively, so `"dat"` and `"DAT"` both validate - the loader always accepted either, but the authoring schema flagged the lower-case spelling as invalid.

### Improvements

- The game schema now declares a version, and the extension checks it. The schema is published separately and updates on its own cadence, so it can get ahead of an installed extension. Until now that failed silently and, worse, sometimes loudly-wrong: a value shape the extension did not recognise was read as an ordinary value, which could report perfectly valid XML as broken. There is now a `schemaVersion` in the schema manifest, checked against the range each extension build understands. A schema whose major version is too new is refused outright with a message telling you to update, rather than half-loaded into wrong answers; an older schema is used as-is; a schema from before the field existed keeps working untouched. Independently of the version check, a tag whose value *shape* the extension cannot interpret now has its validation withheld instead of guessed at - so a newer schema costs you a feature on that tag, never a false error.

### Bug fixes

- **EaWEdit: Re-validate Workspace** re-validates the whole workspace. It re-ran XML diagnostics only - a leftover from when XML was the only language that reported any - so running it to clear a stale Lua or dialog problem reported success while re-checking nothing at all. All three languages are now refreshed, and one of them failing no longer abandons the rest of the sweep.

- Campaign story attachments are validated as you type. Pasting the generic tag's `Faction, PlotFile` tuple into a faction-specific tag - `<Rebel_Story_Name>test, Conquests\Story_Plots_GCMenu.xml</Rebel_Story_Name>` - is now an error that names `<Story_Name>` as the form that takes a tuple, and offers a quick fix that drops the stray faction token. Previously the whole string was taken as a filename, so the mistake could only ever surface as a misleading "file does not exist", and only after a restart or project reload.

- Attaching one faction twice in the same campaign is reported. Naming the same plot manifest through both a `<Rebel_Story_Name>` tag and a `<Story_Name>` slot is a warning (the engine merges both, so one of them is dead weight); pointing them at *different* manifests is an error, because which one the faction ends up running can no longer be read off the file. Paths are compared in their normal form, so `Conquests\X.xml`, `Conquests/X.xml` and `DATA\XML\Conquests\X.xml` count as the same file. A campaign that uses both authoring forms for different factions gets an informational note rather than a warning - only the generic form can attach a non-major faction, so the split is often deliberate.

- The faction half of a `<Story_Name>` tuple is a real reference: Ctrl+Click jumps to the faction definition, rename reaches it, and a faction no `<Faction>` defines is flagged. Only the plot-file half was ever indexed, so a typo in the faction slot resolved to nothing and was silently ignored.

- Broken story-chain links are reported as you type. A `*_Story_Name`, `Active_Plot`, `Suspended_Plot` or tactical plot reference pointing at a missing file was only ever checked during the startup scan, which re-ran on a `.pgproj` change and nothing else - so a link broken after startup stayed silent until the next restart, and one that had since been *fixed* kept being reported. These diagnostics now come from the live campaign chain, which reads unsaved editor content and re-runs whenever any file it touched changes.

- Metafiles shipped by several layers follow the engine's override rule instead of being merged. When a mod and one of its dependencies each ship a `GameObjectFiles.xml`, `CampaignFiles.xml` or any other registry, the game resolves the name to a single file and reads only that one - the mod's copy shadows the dependency's outright, which is why a mod that ships its own registry has to repeat the entries it wants to keep. The extension used to read every layer's copy and combine the results, so files the winning registry leaves out were still typed and indexed, and objects the game never loads still counted as defined. The winning registry can of course still name files that live in a dependency; those continue to resolve across all layers, as do individual campaign, manifest and thread files, which still take the highest-ranked copy.

  Expect this to surface real problems that were previously masked: if your `GameObjectFiles.xml` (or any other registry) omits entries that a dependency's copy listed, references to the objects in those files will now be reported as unknown - which is what the game does too.

## 0.3.1

### Improvements

- The story graph panel opens and navigates very large campaigns smoothly. A campaign with well over a thousand events used to take several seconds to open and stuttered while panning and zooming; it now opens near-instantly and stays responsive throughout. When zoomed out the whole graph is drawn as a lightweight overview - nodes as branch-coloured tiles, with event titles fading in as you zoom - and the real, interactive nodes for the visible area take over once you zoom in close enough to work with them. The minimap, thread and chapter swimlanes, Fit, Arrange, and jump-to-node from the problems list all work against the overview, and Edit mode no longer has to render the whole graph to make a change.

### Bug fixes

- Campaign story chains attached through the generic, additive `Story_Name` tag are discovered, navigated and validated. This is the flat `Faction, PlotFile[, Faction, PlotFile ...]` tuple form that mods such as EaWX use to attach plots (and the only form that can attach a non-major faction); it is now handled alongside the faction-specific `Rebel_Story_Name` / `Empire_Story_Name` / `Underworld_Story_Name` tags, with every occurrence of both forms merged into one (faction, plot) list to match the engine.

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
