// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.CodeActions;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml.Tests.CodeActions;

public sealed class SquadronSyncCodeActionProviderTest
{
    private const string Uri = "file:///test/Squadrons.xml";

    // 5 units across 2 Squadron_Units tags, 0 offsets
    private const string XmlZeroOffsets = """
                                          <Squadrons>
                                            <Squadron>
                                              <Squadron_Units>A, B</Squadron_Units>
                                              <Squadron_Units>C, D, E</Squadron_Units>
                                            </Squadron>
                                          </Squadrons>
                                          """;

    // 5 units, 2 offsets (missing 3)
    private const string XmlTooFewOffsets = """
                                            <Squadrons>
                                              <Squadron>
                                                <Squadron_Units>A, B</Squadron_Units>
                                                <Squadron_Units>C, D, E</Squadron_Units>
                                                <Squadron_Offsets>0, 0, 0</Squadron_Offsets>
                                                <Squadron_Offsets>0, 0, 0</Squadron_Offsets>
                                              </Squadron>
                                            </Squadrons>
                                            """;

    // 2 units, 5 offsets (3 excess)
    private const string XmlTooManyOffsets = """
                                             <Squadrons>
                                               <Squadron>
                                                 <Squadron_Units>A, B</Squadron_Units>
                                                 <Squadron_Offsets>0, 0, 0</Squadron_Offsets>
                                                 <Squadron_Offsets>0, 0, 0</Squadron_Offsets>
                                                 <Squadron_Offsets>0, 0, 0</Squadron_Offsets>
                                                 <Squadron_Offsets>0, 0, 0</Squadron_Offsets>
                                                 <Squadron_Offsets>0, 0, 0</Squadron_Offsets>
                                               </Squadron>
                                             </Squadrons>
                                             """;

    private static SquadronSyncCodeActionProvider BuildProvider(string xml)
    {
        var host = new FakeWorkspaceHost(Uri, xml);
        var fileHelper = new FileHelper(new MockFileSystem());
        return new SquadronSyncCodeActionProvider(TestParseCache.For(host, fileHelper), fileHelper);
    }

    private static XmlCodeActionContext MakeCtx(int diagnosticLine, int expectedOffsets)
    {
        var data = new JObject { ["squadronSync"] = new JObject { ["expectedOffsets"] = expectedOffsets } };
        var diagnostic = new Diagnostic
        {
            Range = new LspRange(new Position(diagnosticLine, 0), new Position(diagnosticLine, 8)),
            Message = "test",
            Data = data
        };
        return new XmlCodeActionContext(DocumentUri.From(Uri), diagnostic);
    }

    [Fact]
    public void No_squadronSync_key_returns_empty()
    {
        var provider = BuildProvider(XmlZeroOffsets);
        var diagnostic = new Diagnostic
        {
            Range = new LspRange(new Position(1, 0), new Position(1, 8)),
            Message = "unrelated"
        };
        var ctx = new XmlCodeActionContext(DocumentUri.From(Uri), diagnostic);
        Assert.Empty(provider.Handle(ctx));
    }

    [Fact]
    public void Zero_offsets_returns_add_missing_and_sync_all()
    {
        var provider = BuildProvider(XmlZeroOffsets);
        // Squadron opening tag is on line 1 (0-based)
        var ctx = MakeCtx(1, 5);
        var actions = provider.Handle(ctx).ToList();
        Assert.Equal(2, actions.Count);
        Assert.Contains(actions, a => a.CodeAction!.Title.StartsWith("Add 5 missing"));
        Assert.Contains(actions, a => a.CodeAction!.Title.StartsWith("Sync all"));
    }

    [Fact]
    public void Too_few_offsets_preferred_action_is_add_missing()
    {
        var provider = BuildProvider(XmlTooFewOffsets);
        var ctx = MakeCtx(1, 5);
        var actions = provider.Handle(ctx).ToList();
        var preferred = actions.FirstOrDefault(a => a.CodeAction?.IsPreferred == true);
        Assert.NotNull(preferred);
        Assert.Contains("Add 3 missing", preferred!.CodeAction!.Title);
    }

    [Fact]
    public void Too_many_offsets_preferred_action_is_remove_excess()
    {
        var provider = BuildProvider(XmlTooManyOffsets);
        var ctx = MakeCtx(1, 2);
        var actions = provider.Handle(ctx).ToList();
        var preferred = actions.FirstOrDefault(a => a.CodeAction?.IsPreferred == true);
        Assert.NotNull(preferred);
        Assert.Contains("Remove 3 excess", preferred!.CodeAction!.Title);
    }

    [Fact]
    public void Too_many_offsets_returns_remove_excess_and_sync_all()
    {
        var provider = BuildProvider(XmlTooManyOffsets);
        var ctx = MakeCtx(1, 2);
        var actions = provider.Handle(ctx).ToList();
        Assert.Equal(2, actions.Count);
        Assert.Contains(actions, a => a.CodeAction!.Title.StartsWith("Remove 3 excess"));
        Assert.Contains(actions, a => a.CodeAction!.Title.StartsWith("Sync all"));
    }

    [Fact]
    public void Add_missing_action_inserts_correct_line_count()
    {
        var provider = BuildProvider(XmlZeroOffsets);
        var ctx = MakeCtx(1, 5);
        var actions = provider.Handle(ctx).ToList();
        var addAction = actions.First(a => a.CodeAction!.Title.StartsWith("Add 5 missing"));
        var edits = GetEdits(addAction);
        Assert.Single(edits);
        var newText = edits[0].NewText;
        Assert.Equal(5, newText.Split("<Squadron_Offsets>").Length - 1);
    }

    [Fact]
    public void Sync_all_action_generates_correct_count_when_replacing_existing()
    {
        var provider = BuildProvider(XmlTooFewOffsets);
        var ctx = MakeCtx(1, 5);
        var actions = provider.Handle(ctx).ToList();
        var syncAction = actions.First(a => a.CodeAction!.Title.StartsWith("Sync all"));
        var edits = GetEdits(syncAction);
        Assert.Single(edits);
        var newText = edits[0].NewText;
        Assert.Equal(5, newText.Split("<Squadron_Offsets>").Length - 1);
    }

    [Fact]
    public void Remove_excess_action_deletes_correct_range()
    {
        var provider = BuildProvider(XmlTooManyOffsets);
        var ctx = MakeCtx(1, 2);
        var actions = provider.Handle(ctx).ToList();
        var removeAction = actions.First(a => a.CodeAction!.Title.StartsWith("Remove 3 excess"));
        var edits = GetEdits(removeAction);
        Assert.Single(edits);
        Assert.Equal("", edits[0].NewText);
    }

    private static IReadOnlyList<TextEdit> GetEdits(CommandOrCodeAction action)
    {
        var changes = action.CodeAction!.Edit!.Changes!;
        return changes.Values.First().ToList();
    }
}

file sealed class FakeWorkspaceHost : IGameWorkspaceHost
{
    private readonly string _normalizedUri;
    private readonly string _text;

    public FakeWorkspaceHost(string uri, string text)
    {
        _normalizedUri = uri;
        _text = text;
    }

    public IEnumerable<TrackedDocument> All => [new(_normalizedUri, _text, 1)];

    public void AddOrUpdate(string uri, string text, int version, bool publishDiagnostics = true)
    {
    }

    public void Remove(string uri)
    {
    }

    public bool TryGet(string uri, out TrackedDocument doc)
    {
        doc = new TrackedDocument(_normalizedUri, _text, 1);
        return true;
    }
}