// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.DependencyInjection;
using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Tests.Diagnostics;

/// <summary>
///     The rule that makes suppression-by-id complete (#68): every registered handler declares a
///     <see cref="IXmlDiagnosticsHandler.DefaultId" />, so no diagnostic can reach the client
///     without a code. Handlers reporting several distinct kinds still set
///     <see cref="XmlDiagnosticResult.Id" /> per result - the default is the floor, not the whole
///     story.
/// </summary>
public sealed class DiagnosticIdCoverageTest
{
    private static IReadOnlyList<IXmlDiagnosticsHandler> RegisteredHandlers()
    {
        var services = new ServiceCollection();
        services.AddXmlLanguageServices();
        return services.BuildServiceProvider().GetServices<IXmlDiagnosticsHandler>().ToList();
    }

    [Fact]
    public void EveryRegisteredHandler_DeclaresADefaultId()
    {
        var missing = RegisteredHandlers()
            .Where(h => h.DefaultId is null)
            .Select(h => h.GetType().Name)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        Assert.Empty(missing);
    }

    // A handful of diagnostics are built directly by XmlDiagnosticsPublisher instead of by a
    // handler, so the reflection check above cannot see them - and they are exactly the ones that
    // would quietly ship without a code. Source-level, because there is no runtime seam that
    // enumerates them.
    [Fact]
    public void EveryPublisherBuiltDiagnostic_SetsACode()
    {
        var source = File.ReadAllText(PublisherSourcePath());

        // Each `new Diagnostic { ... }` object initialiser must assign Code before its closing
        // brace. Splitting on the initialiser keyword keeps this readable without a C# parser.
        //
        // The split must not catch `new DiagnosticRelatedInformation`, which shares the prefix. That
        // false block used to be harmless only by accident - nothing after it in the file contained
        // a `};`, so its body came out empty and was skipped - and it turned into a failure the
        // moment an unrelated object initialiser was added further down. An initialiser is
        // identified by the `{` that follows the type name.
        var blocks = source.Split("new Diagnostic")
            .Skip(1)
            .Where(b => b.TrimStart().StartsWith('{'))
            .ToList();
        var uncoded = blocks
            .Select((b, i) => (Index: i + 1, Body: b[..Math.Max(0, b.IndexOf("};", StringComparison.Ordinal))]))
            .Where(b => b.Body.Length > 0 && !b.Body.Contains("Code =", StringComparison.Ordinal))
            .Select(b => $"#{b.Index}")
            .ToList();

        // A source-scanning check that matches nothing passes for the wrong reason. The publisher
        // has always built several of these by hand; if that ever reaches zero, this test has
        // stopped looking rather than started being satisfied.
        Assert.NotEmpty(blocks);
        Assert.Empty(uncoded);
    }

    private static string PublisherSourcePath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "PG.StarWarsGame.LSP.Xml")))
            dir = Path.GetDirectoryName(dir);

        Assert.NotNull(dir);
        return Path.Combine(dir!, "PG.StarWarsGame.LSP.Xml", "XmlDiagnosticsPublisher.cs");
    }

    // Two handlers sharing a default id would make them indistinguishable to a suppression, and
    // the user could not silence one without the other.
    [Fact]
    public void DefaultIds_AreNotSharedBetweenHandlers()
    {
        var shared = RegisteredHandlers()
            .Where(h => h.DefaultId is not null)
            .GroupBy(h => h.DefaultId!.Value)
            .Where(g => g.Select(h => h.GetType()).Distinct().Count() > 1)
            .Select(g => $"{g.Key} <- {string.Join(", ", g.Select(h => h.GetType().Name).Distinct())}")
            .ToList();

        Assert.Empty(shared);
    }
}