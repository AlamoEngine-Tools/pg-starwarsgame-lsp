// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;
using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Lua.Tests.Diagnostics;

/// <summary>
///     The Lua counterpart of the XML id-coverage rule: a diagnostic that reaches the client
///     without a parseable id cannot be suppressed, and the user has no way to discover why.
///     <para>
///         Source-level, like the XML publisher check it mirrors, because Lua's diagnostics are
///         built inline by the publisher and its analyzers rather than by registered handlers -
///         there is no runtime seam that enumerates them.
///     </para>
/// </summary>
public sealed class LuaDiagnosticIdCoverageTest
{
    private static readonly string[] SourceFiles =
    [
        Path.Combine("Diagnostics", "LuaDiagnosticsPublisher.cs"),
        Path.Combine("Analysis", "LuaGlobalScopeAnalyzer.cs"),
        Path.Combine("Analysis", "LuaUpvalueAnalyzer.cs"),
        Path.Combine("Analysis", "LuaImportAnalyzer.cs")
    ];

    private static string LuaProjectDirectory()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "PG.StarWarsGame.LSP.Lua")))
            dir = Path.GetDirectoryName(dir);

        Assert.NotNull(dir);
        return Path.Combine(dir!, "PG.StarWarsGame.LSP.Lua");
    }

    // Each `new LspDiagnostic { ... }` object initialiser must assign Code before its closing
    // brace. Splitting on the initialiser keyword keeps this readable without a C# parser - the
    // same approach the XML coverage test uses.
    [Fact]
    public void EveryLuaDiagnostic_SetsACode()
    {
        var root = LuaProjectDirectory();
        var uncoded = new List<string>();

        foreach (var relative in SourceFiles)
        {
            var path = Path.Combine(root, relative);
            if (!File.Exists(path)) continue;

            // Anchored on the opening brace so sibling types - LspDiagnosticContainer and the like -
            // are not mistaken for a diagnostic being constructed.
            var blocks = Regex.Split(File.ReadAllText(path), @"new LspDiagnostic\s*\{")
                .Skip(1)
                .ToList();

            for (var i = 0; i < blocks.Count; i++)
            {
                var end = blocks[i].IndexOf("};", StringComparison.Ordinal);
                if (end <= 0) continue;
                if (!blocks[i][..end].Contains("Code =", StringComparison.Ordinal))
                    uncoded.Add($"{relative} #{i + 1}");
            }
        }

        Assert.Empty(uncoded);
    }

    // The reserved band is what keeps the Loretta mapping and any hand-assigned syntax id from
    // ever colliding. Nothing in the catalogue may sit below it in the Syntax group.
    [Fact]
    public void NoCataloguedSyntaxId_SitsInTheBandReservedForLoretta()
    {
        var offenders = typeof(DiagnosticIds)
            .GetFields()
            .Where(f => f.FieldType == typeof(DiagnosticId))
            .Select(f => (f.Name, Id: (DiagnosticId)f.GetValue(null)!))
            .Where(x => x.Id.Group == (int)DiagnosticGroup.Syntax
                        && x.Id.Number < Lua.Diagnostics.LorettaDiagnosticIds.FirstOwnSyntaxNumber)
            .Select(x => $"{x.Name} = {x.Id}")
            .ToList();

        Assert.Empty(offenders);
    }
}
