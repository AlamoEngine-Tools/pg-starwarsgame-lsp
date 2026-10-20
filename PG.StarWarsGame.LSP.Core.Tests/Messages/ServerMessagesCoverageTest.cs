// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PG.StarWarsGame.LSP.Core.Tests.Messages;

/// <summary>
///     The catalogue's keys are strings, so a typo compiles. This ties every key the server's code
///     asks for to the catalogue and back: a missing key would throw in front of an author, and an
///     unused one is a text nobody can see that translators would still be asked for.
/// </summary>
public sealed partial class ServerMessagesCoverageTest
{
    [GeneratedRegex(@"[Mm]essages\.Format\(\s*""([A-Za-z0-9_]+)""")]
    private static partial Regex KeyUse();

    /// <summary>A key a handler names through a property - <c>ExpectationKey =&gt; "..."</c>.</summary>
    [GeneratedRegex(@"Key\s*=>\s*""([A-Za-z0-9_]+)""")]
    private static partial Regex KeyProperty();

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.EnumerateFiles("*.slnx").Any())
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No solution above the test binaries.");
    }

    private static IReadOnlySet<string> CatalogueKeys(string root)
    {
        var resx = Path.Combine(root, "PG.StarWarsGame.LSP.Core", "Resources", "ServerMessages.resx");
        return XDocument.Load(resx).Root!.Elements("data").Select(d => (string)d.Attribute("name")!).ToHashSet();
    }

    private static IReadOnlySet<string> KeysInCode(string root)
    {
        return Directory.EnumerateDirectories(root, "PG.StarWarsGame.LSP.*")
            .Where(d => !d.EndsWith(".Tests") && !d.Contains("Client"))
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f =>
            {
                var source = File.ReadAllText(f);
                return KeyUse().Matches(source).Concat(KeyProperty().Matches(source)).Select(m => m.Groups[1].Value);
            })
            .ToHashSet();
    }

    [Fact]
    public void EveryKeyTheCodeAsksForIsInTheCatalogue()
    {
        var root = RepositoryRoot();
        Assert.Empty(KeysInCode(root).Except(CatalogueKeys(root)).Order());
    }

    [Fact]
    public void EveryKeyInTheCatalogueIsAskedFor()
    {
        var root = RepositoryRoot();
        Assert.Empty(CatalogueKeys(root).Except(KeysInCode(root)).Order());
    }
}