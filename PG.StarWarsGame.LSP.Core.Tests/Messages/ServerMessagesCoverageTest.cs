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

    /// <summary>A key handed on unformatted - <c>new CatalogueText("...")</c>.</summary>
    [GeneratedRegex(@"new CatalogueText\(\s*""([A-Za-z0-9_]+)""")]
    private static partial Regex KeyText();

    /// <summary>
    ///     Any literal shaped like a key - <c>"Prefix_Name"</c>. Only asked whether a catalogue key is
    ///     used: a key passed on as a plain argument, such as a fact's key field, has no call shape to
    ///     recognise, and the formatter strips the argument name that would give it one.
    /// </summary>
    [GeneratedRegex(@"""([A-Za-z][A-Za-z0-9]*_[A-Za-z0-9_]+)""")]
    private static partial Regex KeyLiteral();

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

    private static IEnumerable<string> ServerSources(string root)
    {
        return Directory.EnumerateDirectories(root, "PG.StarWarsGame.LSP.*")
            .Where(d => !d.EndsWith(".Tests") && !d.Contains("Client"))
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(File.ReadAllText);
    }

    private static IReadOnlySet<string> KeysAskedFor(string root)
    {
        return ServerSources(root)
            .SelectMany(source => KeyUse().Matches(source).Concat(KeyProperty().Matches(source))
                .Concat(KeyText().Matches(source)).Select(m => m.Groups[1].Value))
            .ToHashSet();
    }

    [Fact]
    public void EveryKeyTheCodeAsksForIsInTheCatalogue()
    {
        var root = RepositoryRoot();
        Assert.Empty(KeysAskedFor(root).Except(CatalogueKeys(root)).Order());
    }

    [Fact]
    public void EveryKeyInTheCatalogueIsAskedFor()
    {
        var root = RepositoryRoot();
        var literals = ServerSources(root)
            .SelectMany(source => KeyLiteral().Matches(source).Select(m => m.Groups[1].Value));
        Assert.Empty(CatalogueKeys(root).Except(KeysAskedFor(root)).Except(literals).Order());
    }
}
