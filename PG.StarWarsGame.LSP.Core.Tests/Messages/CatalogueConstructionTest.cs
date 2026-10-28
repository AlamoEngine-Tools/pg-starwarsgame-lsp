// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;

namespace PG.StarWarsGame.LSP.Core.Tests.Messages;

/// <summary>
///     The server has ONE message catalogue, <see cref="Core.Messages.IMessageCatalog" />, registered
///     once and injected everywhere a text is formatted (#306). A class that builds its own, or a
///     method that falls back to English when it was handed none, speaks a language the setting does
///     not control and cannot be given another catalogue by a test. This finds both.
/// </summary>
/// <remarks>
///     Three sites may construct a catalogue, each being a process root rather than a consumer:
///     <c>ConfiguredMessageCatalog</c>, the language server's catalogue over its configuration;
///     <c>LuaDebugAdapter</c>, which runs as its own process and takes its locale from the debug
///     client's <c>initialize</c>; and <c>ICatalogueTextError.English</c>, the English of an
///     exception's own message, which goes to the log and not to a reader.
/// </remarks>
public sealed partial class CatalogueConstructionTest
{
    private static readonly string[] CatalogueRoots =
    [
        Path.Combine("PG.StarWarsGame.LSP.Core", "Messages", "ResxMessageCatalog.cs"),
        Path.Combine("PG.StarWarsGame.LSP.Core", "Messages", "ConfiguredMessageCatalog.cs"),
        Path.Combine("PG.StarWarsGame.LSP.Core", "Messages", "ICatalogueTextError.cs"),
        Path.Combine("PG.StarWarsGame.LSP.Lua.Debug", "Dap", "LuaDebugAdapter.cs")
    ];

    [GeneratedRegex(@"ResxMessageCatalog\.Server\(")]
    private static partial Regex ResxConstruction();

    [GeneratedRegex(@"new ConfiguredMessageCatalog\(")]
    private static partial Regex ConfiguredConstruction();

    /// <summary>An optional catalogue parameter, or a catalogue property with a default: both hide an English fallback.</summary>
    [GeneratedRegex(@"IMessageCatalog\?\s+\w+\s*=\s*null|IMessageCatalog\s+\w+\s*\{[^}]*\}\s*=")]
    private static partial Regex CatalogueFallback();

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.EnumerateFiles("*.slnx").Any())
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No solution above the test binaries.");
    }

    /// <summary>Every production source: the server projects without their tests, the client, the baseline builder and the test support.</summary>
    private static IEnumerable<string> ProductionSources(string root)
    {
        return Directory.EnumerateDirectories(root, "PG.StarWarsGame.LSP.*")
            .Where(d => !d.EndsWith(".Tests") && !d.EndsWith(".Testing") && !d.Contains("Client")
                        && !d.Contains("BaselineBuilder"))
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
    }

    private static IEnumerable<string> Sites(string root, Regex pattern, IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            var lines = text.Split('\n');
            foreach (Match match in pattern.Matches(text))
            {
                var line = text[..match.Index].Count(c => c == '\n');
                if (lines[line].TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                yield return $"{Path.GetRelativePath(root, file)}:{line + 1} {lines[line].Trim()}";
            }
        }
    }

    [Fact]
    public void OnlyTheProcessRootsConstructACatalogue()
    {
        var root = RepositoryRoot();
        var consumers = ProductionSources(root)
            .Where(f => !CatalogueRoots.Any(allowed => f.EndsWith(allowed, StringComparison.Ordinal)));

        Assert.Empty(Sites(root, ResxConstruction(), consumers));
    }

    [Fact]
    public void NothingConstructsTheConfiguredCatalogueByHand()
    {
        var root = RepositoryRoot();

        Assert.Empty(Sites(root, ConfiguredConstruction(), ProductionSources(root)));
    }

    /// <summary>
    ///     The roots are exempt here too: the debug adapter speaks English until the client's
    ///     <c>initialize</c> names a locale, and that default is the root's decision, not a fallback.
    /// </summary>
    [Fact]
    public void NoCatalogueParameterOrPropertyFallsBackToEnglish()
    {
        var root = RepositoryRoot();
        var consumers = ProductionSources(root)
            .Where(f => !CatalogueRoots.Any(allowed => f.EndsWith(allowed, StringComparison.Ordinal)));

        Assert.Empty(Sites(root, CatalogueFallback(), consumers));
    }
}