// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;
using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Server.Encyclopedia;

namespace PG.StarWarsGame.LSP.Server.Tests.Protocol;

/// <summary>
///     The custom <c>aet/*</c> endpoints, checked from both ends.
/// </summary>
/// <remarks>
///     <para>
///         The method name is the thinnest part of the whole contract and the least protected. A DTO
///         that drifts shows up as an undefined field; a method name that drifts shows up as nothing
///         at all - the request goes out, no handler matches, and the panel is simply empty. Nothing
///         in either language fails to compile.
///     </para>
///     <para>
///         This covers the surfaces <c>PreviewWireShapeTest</c> and <c>StoryWireKindTest</c> do not:
///         encyclopedia, localisation and workspace have no enums worth walking, but all three are
///         reached by name. Rather than mirror each DTO field by hand, this guards the handle every
///         one of them hangs from.
///     </para>
/// </remarks>
public sealed class ProtocolMethodContractTest
{
    private static readonly string RepoRoot = FindRepoRoot();

    /// <summary>
    ///     Method names the client builds at run time rather than writing out.
    /// </summary>
    /// <remarks>
    ///     <c>storyGraphPanel.ts</c> composes the simulator's verbs:
    ///     <c>'aet/storySim' + method.charAt(0).toUpperCase() + method.slice(1)</c>. So fourteen
    ///     server endpoints are called without any of their names appearing in the client, and the
    ///     prefix appears without being a method. A literal-only comparison reports every one of
    ///     those as drift in one direction or the other, which is why the prefix is declared here
    ///     instead.
    /// </remarks>
    private static readonly string[] ClientDynamicPrefixes = ["aet/storySim"];

    /// <summary>
    ///     Endpoints the server serves that no client code path reaches, and why that is allowed.
    /// </summary>
    /// <remarks>
    ///     <c>aet/getStoryDiagnostics</c> was left behind by the move to staged editing: the panel
    ///     now dry-runs the pending batch through <c>aet/validateStoryCommandBatch</c> and reads the
    ///     same <c>GetStoryDiagnosticsResult</c> back, so the original endpoint is still handled and
    ///     still tested but nothing calls it. Listed rather than deleted because that is the
    ///     maintainer's call, not this test's - but listed, so it cannot quietly become three.
    /// </remarks>
    private static readonly HashSet<string> ServerMethodsNoClientCalls =
        new(StringComparer.Ordinal) { "aet/getStoryDiagnostics" };

    /// <summary>A request the client makes has somewhere to land.</summary>
    [Fact]
    public void EveryMethodTheClientCalls_IsDeclaredByTheServer()
    {
        var server = ServerMethods();

        var undeliverable = ClientMethods()
            .Where(m => !server.Contains(m) && !IsDynamicPrefix(m, server))
            .OrderBy(m => m, StringComparer.Ordinal)
            .ToList();

        Assert.True(undeliverable.Count == 0,
            $"Client calls no server handler answers: {string.Join(", ", undeliverable)}. The request "
            + "goes out and nothing replies, so the feature is silently dead. Either the server "
            + "method was renamed, or the name is built at run time and belongs in "
            + "ClientDynamicPrefixes.");
    }

    /// <summary>An endpoint the server serves is reachable from the client, or known not to be.</summary>
    [Fact]
    public void EveryServerMethod_IsReachedByTheClientOrDeclaredUnused()
    {
        var client = ClientMethods();

        var unreached = ServerMethods()
            .Where(m => !client.Contains(m)
                        && !ClientDynamicPrefixes.Any(p => m.StartsWith(p, StringComparison.Ordinal))
                        && !ServerMethodsNoClientCalls.Contains(m))
            .OrderBy(m => m, StringComparer.Ordinal)
            .ToList();

        Assert.True(unreached.Count == 0,
            $"Server endpoint(s) nothing in the client calls: {string.Join(", ", unreached)}. Either "
            + "the client's call site was renamed and the feature is broken, or the endpoint is dead "
            + "and belongs in ServerMethodsNoClientCalls with the reason it is kept.");
    }

    /// <summary>
    ///     The source scan sees everything the attributes declare.
    /// </summary>
    /// <remarks>
    ///     The server names a method three ways - a <c>[Method]</c> attribute (most of them), a
    ///     <c>const string</c> on <c>IPgprojMigrationPrompt</c>, and two <c>SendNotification</c> calls
    ///     in <c>ServerConfigurator</c>. Reflection alone would miss the last three, so the sets above
    ///     come from a source scan. This checks that scan against the one authoritative source there
    ///     is, so a scan that quietly stopped matching would fail here rather than passing everything.
    /// </remarks>
    [Fact]
    public void TheSourceScan_FindsEveryMethodTheAttributesDeclare()
    {
        var scanned = ServerMethods();

        var declared = typeof(GetEncyclopediaEntryParams).Assembly.GetTypes()
            .SelectMany(t => t.GetCustomAttributes(typeof(MethodAttribute), false).Cast<MethodAttribute>())
            .Select(a => a.Method)
            .Where(m => m.StartsWith("aet/", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(declared);

        var missed = declared.Where(m => !scanned.Contains(m)).OrderBy(m => m, StringComparer.Ordinal).ToList();
        Assert.True(missed.Count == 0,
            $"The source scan missed method(s) a [Method] attribute declares: "
            + $"{string.Join(", ", missed)}. The scan's regex no longer matches how they are written.");
    }

    /// <summary>A declared dynamic prefix still stands for real endpoints.</summary>
    /// <remarks>
    ///     A prefix exempts every server method beneath it, so one left behind after its endpoints
    ///     were renamed would hide their disappearance rather than report it.
    /// </remarks>
    [Fact]
    public void EveryDynamicPrefix_StillCoversServerMethods()
    {
        var server = ServerMethods();

        foreach (var prefix in ClientDynamicPrefixes)
            Assert.True(
                server.Any(m => m.Length > prefix.Length && m.StartsWith(prefix, StringComparison.Ordinal)),
                $"'{prefix}' is declared as a dynamic prefix but no server method starts with it. "
                + "Drop it, or correct it to whatever the endpoints are called now.");
    }

    // ── reading both sides ───────────────────────────────────────────────────

    /// <summary>Every <c>aet/*</c> name the server project spells, however it is declared.</summary>
    private static HashSet<string> ServerMethods()
    {
        var root = Path.Combine(RepoRoot, "PG.StarWarsGame.LSP.Server");
        var methods = Scan(root, ["*.cs"], @"""(aet/[A-Za-z]+)""", _ => false);

        Assert.NotEmpty(methods);
        return methods;
    }

    /// <summary>
    ///     Every <c>aet/*</c> name the shipped client spells.
    /// </summary>
    /// <remarks>
    ///     Test files are excluded: <c>lspGateway.test.ts</c> drives the transport with stand-ins
    ///     (<c>aet/x</c>, <c>aet/anything</c>) that are deliberately not endpoints, and reading them
    ///     as calls would make this test fail on code that is doing the right thing.
    /// </remarks>
    private static HashSet<string> ClientMethods()
    {
        var root = Path.Combine(
            RepoRoot, "PG.StarWarsGame.LSP.Client.VSCode", "aet-eaw-edit", "src");
        var methods = Scan(root, ["*.ts", "*.tsx"], @"'(aet/[A-Za-z]+)'",
            f => f.Contains(".test.", StringComparison.Ordinal));

        Assert.NotEmpty(methods);
        return methods;
    }

    private static HashSet<string> Scan(
        string root, string[] patterns, string regex, Func<string, bool> skip)
    {
        Assert.True(Directory.Exists(root), $"Source root not found: {root}");

        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pattern in patterns)
        foreach (var file in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
        {
            if (skip(Path.GetFileName(file))) continue;
            foreach (Match m in Regex.Matches(File.ReadAllText(file), regex))
                found.Add(m.Groups[1].Value);
        }

        return found;
    }

    private static bool IsDynamicPrefix(string method, HashSet<string> server)
    {
        // The prefix itself is not a method; it is the literal the client concatenates onto. It is
        // legitimate only while real endpoints sit beneath it.
        return ClientDynamicPrefixes.Contains(method, StringComparer.Ordinal)
               && server.Any(m => m.Length > method.Length
                                  && m.StartsWith(method, StringComparison.Ordinal));
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
