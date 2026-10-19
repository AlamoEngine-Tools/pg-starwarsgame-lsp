// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Checks a game object's <c>Lua_Script</c> against the file the game would load for it.
/// </summary>
/// <remarks>
///     <para>
///         A name that loads nothing is an error even though the game carries on: the object runs
///         without its script, which is never what the author wrote the tag for. A name that only
///         loads on some setups is an error for the same reason - a <c>..</c> works for a loose
///         file on Windows and never inside an archive, which is how mods ship for Linux.
///     </para>
///     <para>
///         A story manifest's <c>Lua_Script</c> is loaded the same way but expected under
///         <c>Data/Scripts/Story</c>, so it is not checked here.
///     </para>
/// </remarks>
public sealed class LuaScriptReferenceHandler : XmlDiagnosticsHandler<XmlTagValueFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.LuaScriptExistence;

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (fact.Tag.ReferenceKind != ReferenceKind.WorkspaceFile ||
            !string.Equals(fact.Tag.ReferenceTypeName, WorkspaceFileKey.LuaScriptType,
                StringComparison.OrdinalIgnoreCase))
            return [];

        // Null is "cannot tell", not "a game object".
        if (fact.OwningType is not { } owner ||
            string.Equals(owner, StoryReferenceTypes.PlotManifestFileTypeName, StringComparison.OrdinalIgnoreCase))
            return [];

        var name = fact.RawValue;

        // These three never load in every setup, whether or not a file of a similar name exists,
        // so they are reported before the lookup rather than instead of a miss.
        if (name.Contains('\\'))
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"Lua script '{name}' contains a backslash. The game reads the name inside a Lua string, where a backslash is an escape, so the script never loads. '/' separates directories.",
                    Id: DiagnosticIds.LuaScriptBackslash)
            ];

        if (name.Split('/').Contains(".."))
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"Lua script '{name}' climbs out of the script directory with '..'. That resolves for a loose file on Windows only, never inside a MEG archive.",
                    Id: DiagnosticIds.LuaScriptParentPath)
            ];

        if (name.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".lc", StringComparison.OrdinalIgnoreCase))
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"Lua script '{name}' carries its extension. The game appends '.lua' itself and looks for '{name}.lua'.",
                    Id: DiagnosticIds.LuaScriptExtension)
            ];

        var candidates = LuaScriptLookup.Candidates(ctx.Index.AssetFiles, name).ToList();
        if (candidates.Count == 0)
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"Lua script '{name}' was not found: no '{name}.lua' or '{name}.lc' in any script directory. The object runs without a script.",
                    Id: DiagnosticIds.LuaScriptExistence)
            ];

        var loaded = candidates[0];
        if (LuaScriptLookup.IsInGameObjectDirectory(loaded))
            return [];

        var shadowed = candidates.FirstOrDefault(LuaScriptLookup.IsInGameObjectDirectory);
        var message = shadowed is null
            ? $"Lua script '{name}' loads from '{loaded}', outside {LuaScriptLookup.GameObjectDirectory.TrimEnd('/')}."
            : $"Lua script '{name}' loads from '{loaded}', which shadows '{shadowed}': the game searches that directory first.";
        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning, message,
                Id: DiagnosticIds.LuaScriptOutsideGameObject)
        ];
    }
}