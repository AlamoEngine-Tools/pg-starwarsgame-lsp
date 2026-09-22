// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;
using PG.StarWarsGame.LSP.Story.Graph;

namespace PG.StarWarsGame.LSP.Server.Tests.Story;

/// <summary>
///     The story graph's kind vocabulary, as the client actually reads it.
/// </summary>
/// <remarks>
///     <para>
///         The counterpart to <c>PreviewWireShapeTest</c> for the story surface. The preview protocol
///         keeps its C# enums and adapts the transport with a converter, so a forgotten enum shows up
///         as an ordinal and the converter test catches it. The story protocol takes the other route:
///         <c>StoryGraphProjection</c> calls <c>.ToString()</c> at the boundary and the DTO field is a
///         plain <c>string</c>. That sidesteps the ordinal trap entirely - and with it every check the
///         converter test performs, because no converter is involved.
///     </para>
///     <para>
///         What is left unguarded is the vocabulary itself. Rename an enum member and C# compiles, the
///         wire quietly changes, and the client's comparison stops matching - the failure mode the
///         preview serializer's remarks describe, where every switch falls through to its default.
///         These tests read the client's own source and compare it to the enums.
///     </para>
///     <para>
///         Note what is deliberately NOT asserted: that every node kind is handled. <c>lodShape.ts</c>
///         says an unrecognised kind "stays a rectangle, so a kind added later degrades quietly rather
///         than claiming a shape it has not been given". Demanding a branch per kind would break that
///         on purpose. Node kinds are therefore checked in one direction only - a literal the client
///         reads must mean something - while edge kinds are checked both ways, because the palette
///         does claim one entry per kind.
///     </para>
/// </remarks>
public sealed class StoryWireKindTest
{
    private static readonly string RepoRoot = FindRepoRoot();

    /// <summary>
    ///     Kinds the CLIENT invents and the server neither sends nor knows about.
    /// </summary>
    /// <remarks>
    ///     Staging junctions are AND/OR nodes the user drops on the canvas in Edit mode before wiring
    ///     them to anything. <c>storyGraph.tsx</c> builds them locally with ids of the form
    ///     <c>local:and:N</c>, and <c>discardStagingJunction</c> is documented "never sent to the
    ///     server". They are listed here rather than treated as drift: without this list a check like
    ///     the one below reads them as client literals with no server member and reports a defect,
    ///     which is exactly the false positive this file exists to prevent.
    /// </remarks>
    private static readonly HashSet<string> ClientOnlyNodeKinds =
        new(StringComparer.Ordinal) { "StagingAnd", "StagingOr" };

    /// <summary>
    ///     Edge kinds allowed to reach the canvas without a colour key entry, and why.
    /// </summary>
    /// <remarks>
    ///     Empty on purpose. An edge kind the reader can see but cannot look up is the gap this
    ///     guards; anything added here needs a reason that survives someone asking "what is that grey
    ///     line?".
    /// </remarks>
    private static readonly HashSet<string> EdgeKindsExemptFromTheColourKey =
        new(StringComparer.Ordinal);

    /// <summary>
    ///     Every edge kind the server emits is drawn from the palette and appears in the colour key.
    /// </summary>
    /// <remarks>
    ///     <c>palette.ts</c> states the invariant itself: one entry per kind, "read by the stroke
    ///     rules AND by the colour key, so a swatch cannot come to disagree with the edge it
    ///     describes". An alias counts - <c>canvasEdgeStyle.ts</c> points <c>TacticalEntry</c> at
    ///     <c>Tactical</c>, so it inherits a swatch that names it fairly.
    /// </remarks>
    [Fact]
    public void EveryEdgeKindTheServerEmits_HasASwatchInTheColourKey()
    {
        var palette = PaletteEdgeKinds();
        var aliased = AliasedEdgeKinds();

        var unreachable = Enum.GetNames<StoryEdgeKind>()
            .Where(k => !palette.Contains(k)
                        && !aliased.Contains(k)
                        && !EdgeKindsExemptFromTheColourKey.Contains(k))
            .ToList();

        Assert.True(unreachable.Count == 0,
            $"StoryEdgeKind member(s) the client draws without a colour key entry: "
            + $"{string.Join(", ", unreachable)}. The server emits these, so a reader sees the edge "
            + "and cannot look it up. Add an EDGE_KINDS entry in "
            + "src/webview/storyGraph/palette.ts, alias it in canvasEdgeStyle.ts, or exempt it here "
            + "with a reason.");
    }

    /// <summary>The colour key never advertises an edge kind the server cannot produce.</summary>
    [Fact]
    public void EveryEdgeKindInTheColourKey_IsARealServerKind()
    {
        var server = Enum.GetNames<StoryEdgeKind>().ToHashSet(StringComparer.Ordinal);

        var invented = PaletteEdgeKinds().Where(k => !server.Contains(k)).ToList();

        Assert.True(invented.Count == 0,
            $"EDGE_KINDS entr(ies) with no StoryEdgeKind member: {string.Join(", ", invented)}. "
            + "The colour key would offer the reader a legend for an edge that never arrives.");
    }

    /// <summary>
    ///     Every node kind the client compares against is a server kind or a declared client-only one.
    /// </summary>
    /// <remarks>
    ///     The direction that catches a rename: the C# side still compiles, but the literal the client
    ///     tests for stops matching anything and its branch goes dead without a word.
    /// </remarks>
    [Fact]
    public void EveryNodeKindTheClientReads_IsAServerKindOrDeclaredClientOnly()
    {
        var server = Enum.GetNames<StoryNodeKind>().ToHashSet(StringComparer.Ordinal);

        var orphaned = ClientNodeKindLiterals()
            .Where(k => !server.Contains(k) && !ClientOnlyNodeKinds.Contains(k))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.True(orphaned.Count == 0,
            $"Node kind literal(s) the client reads that no StoryNodeKind member produces: "
            + $"{string.Join(", ", orphaned)}. Either the enum member was renamed and this branch is "
            + "now dead, or the kind is client-only and belongs in ClientOnlyNodeKinds with a note "
            + "saying so.");
    }

    /// <summary>The client-only list does not outlive the code that needs it.</summary>
    /// <remarks>
    ///     If a staging kind is ever promoted to a real server kind, or the Edit-mode junctions are
    ///     removed, this list has to move with it - otherwise it silently exempts a name that has
    ///     become drift again.
    /// </remarks>
    [Fact]
    public void EveryDeclaredClientOnlyKind_IsStillReadByTheClientAndUnknownToTheServer()
    {
        var literals = ClientNodeKindLiterals();
        var server = Enum.GetNames<StoryNodeKind>().ToHashSet(StringComparer.Ordinal);

        foreach (var kind in ClientOnlyNodeKinds)
        {
            Assert.True(literals.Contains(kind),
                $"'{kind}' is declared client-only but the client no longer reads it. Drop it from "
                + "ClientOnlyNodeKinds.");
            Assert.False(server.Contains(kind),
                $"'{kind}' is declared client-only but StoryNodeKind now has a member of that name. "
                + "Remove the exemption so the pair is checked like every other kind.");
        }
    }

    // ── reading the client ───────────────────────────────────────────────────

    private static string ClientFile(params string[] relative)
    {
        var path = Path.Combine(
            new[] { RepoRoot, "PG.StarWarsGame.LSP.Client.VSCode", "aet-eaw-edit", "src", "webview" }
                .Concat(relative).ToArray());
        Assert.True(File.Exists(path), $"Client source not found: {path}");
        return File.ReadAllText(path);
    }

    /// <summary>The kinds EDGE_KINDS declares, which is what the colour key renders from.</summary>
    private static HashSet<string> PaletteEdgeKinds()
    {
        var text = ClientFile("storyGraph", "palette.ts");
        var block = Regex.Match(text, @"EDGE_KINDS[^=]*=\s*\[(.*?)\n\];", RegexOptions.Singleline);
        Assert.True(block.Success,
            "Could not find the EDGE_KINDS array in palette.ts - this test parses it, so a change to "
            + "its shape needs a change here.");

        var kinds = Regex.Matches(block.Groups[1].Value, @"\{\s*kind:\s*'([A-Za-z]+)'")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(kinds);
        return kinds;
    }

    /// <summary>Kinds given another kind's style outright, so they inherit its swatch.</summary>
    private static HashSet<string> AliasedEdgeKinds()
    {
        var text = ClientFile("storyGraph", "canvasEdgeStyle.ts");
        return Regex.Matches(text, @"kindStyles\.set\(\s*'([A-Za-z]+)'")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    ///     Every node kind literal the client compares a node against.
    /// </summary>
    /// <remarks>
    ///     Read from the three places that switch on a node's kind: the zoomed-out shape table, the
    ///     junction sets the simulator folds over, and the node comparisons in the graph itself.
    ///     Deliberately narrow - <c>storyGraph.tsx</c> also matches on command payload kinds
    ///     (<c>renameEvent</c>) and simulator kinds (<c>battle</c>, <c>planet</c>), which are
    ///     different vocabularies and would be noise here.
    /// </remarks>
    /// <remarks>
    ///     The receiver is what separates a node comparison from an edge one, so it is matched
    ///     explicitly. <c>k.kind === 'Tactical'</c> at the bottom of <c>storyGraph.tsx</c> iterates
    ///     EDGE_KINDS while building stroke selectors; swept up as a node literal it reports
    ///     <c>Tactical</c> as drift, which is a false positive this test produced about itself before
    ///     the receiver was pinned down.
    /// </remarks>
    private static HashSet<string> ClientNodeKindLiterals()
    {
        var kinds = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match m in Regex.Matches(ClientFile("storyGraph", "lodShape.ts"), @"case\s*'([A-Za-z]+)'"))
            kinds.Add(m.Groups[1].Value);

        var simModel = ClientFile("storyGraph", "simModel.ts");
        foreach (Match set in Regex.Matches(simModel, @"(?:AND|OR)_KINDS\s*=\s*new Set\(\[([^\]]*)\]"))
        foreach (Match m in Regex.Matches(set.Groups[1].Value, @"'([A-Za-z]+)'"))
            kinds.Add(m.Groups[1].Value);

        foreach (Match m in Regex.Matches(ClientFile("storyGraph.tsx"),
                     @"\b(?:dto|node|n)\.kind === '([A-Z][A-Za-z]*)'"))
            kinds.Add(m.Groups[1].Value);

        Assert.NotEmpty(kinds);
        return kinds;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PG.StarWarsGame.LSP.slnx")))
            dir = dir.Parent;
        return dir?.FullName
               ?? throw new InvalidOperationException(
                   "Could not locate repo root (PG.StarWarsGame.LSP.slnx) above the test output directory.");
    }
}