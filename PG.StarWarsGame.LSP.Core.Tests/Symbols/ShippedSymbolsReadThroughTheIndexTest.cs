// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;

namespace PG.StarWarsGame.LSP.Core.Tests.Symbols;

/// <summary>
///     The baseline holds every shipped definition, loaded or not; <c>GameIndex.ShippedSymbols</c>
///     holds the ones the game loads. A feature that reads the baseline's symbols directly shows the
///     Executor in an Empire at War project, so only a check that a baseline is loaded at all may.
/// </summary>
public sealed partial class ShippedSymbolsReadThroughTheIndexTest
{
    [GeneratedRegex(@"Baseline\.Symbols(?!\.(Count|IsEmpty)\b)")]
    private static partial Regex DirectRead();

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.EnumerateFiles("*.slnx").Any())
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No solution above the test binaries.");
    }

    [Fact]
    public void NoFeatureReadsTheBaselineSymbolsDirectly()
    {
        var root = RepositoryRoot();
        var offenders = Directory.EnumerateDirectories(root, "PG.StarWarsGame.LSP.*")
            .Where(d => !d.EndsWith(".Tests") && !d.Contains("Client") && !d.Contains("BaselineBuilder") &&
                        !d.EndsWith(".Assets") && !d.EndsWith(".Testing"))
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (File: f, Line: i + 1, Text: line)))
            .Where(l => DirectRead().IsMatch(l.Text))
            .Select(l => $"{Path.GetRelativePath(root, l.File)}:{l.Line}")
            .ToList();

        Assert.Empty(offenders);
    }
}