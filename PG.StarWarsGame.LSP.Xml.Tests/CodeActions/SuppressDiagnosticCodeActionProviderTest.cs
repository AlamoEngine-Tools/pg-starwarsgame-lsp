// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Diagnostics.Suppression;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.CodeActions;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml.Tests.CodeActions;

public sealed class SuppressDiagnosticCodeActionProviderTest
{
    private const string Uri = "file:///test/Units.xml";

    //  line 0: <Units>
    //  line 1:   <Unit Name="A">
    //  line 2:     <Icon_Name>missing.tga</Icon_Name>
    //  line 3:   </Unit>
    //  line 4: </Units>
    private const string Xml = """
                               <Units>
                                 <Unit Name="A">
                                   <Icon_Name>missing.tga</Icon_Name>
                                 </Unit>
                               </Units>
                               """;

    private static SuppressDiagnosticCodeActionProvider BuildProvider()
    {
        var host = new FakeSuppressionWorkspaceHost(Uri, Xml);
        var fileHelper = new FileHelper(new MockFileSystem());
        return new SuppressDiagnosticCodeActionProvider(
            TestParseCache.For(host, fileHelper), fileHelper, new UnitObjectSchema());
    }

    private static XmlCodeActionContext Ctx(string? code, int line = 2)
    {
        var diagnostic = new Diagnostic
        {
            Range = new LspRange(new Position(line, 4), new Position(line, 13)),
            Message = "asset missing",
            Code = code is null ? (DiagnosticCode?)null : new DiagnosticCode(code)
        };
        return new XmlCodeActionContext(DocumentUri.From(Uri), diagnostic);
    }

    private static IReadOnlyList<CodeAction> Actions(string? code, int line = 2)
    {
        return BuildProvider().Handle(Ctx(code, line))
            .Select(a => a.CodeAction!)
            .ToList();
    }

    // A diagnostic with no id cannot be named by a suppression, so offering one would produce a
    // comment that silences nothing.
    [Theory]
    [InlineData(null)]
    [InlineData("story-chain")]
    [InlineData("not-an-id")]
    public void DiagnosticWithoutAnId_OffersNothing(string? code)
    {
        Assert.Empty(Actions(code));
    }

    [Fact]
    public void AllFourScopes_AreOffered()
    {
        var titles = Actions("aetswg-004-0001").Select(a => a.Title).ToList();

        Assert.Equal(4, titles.Count);
        Assert.Contains(titles, t => t.Contains("this line"));
        Assert.Contains(titles, t => t.Contains("<Unit>"));
        Assert.Contains(titles, t => t.Contains("this file"));
        Assert.Contains(titles, t => t.Contains("across the project"));
    }

    // Narrowest first: whichever the user picks by reflex should be the least destructive.
    [Fact]
    public void NarrowestScope_IsOfferedFirst()
    {
        Assert.Contains("this line", Actions("aetswg-004-0001")[0].Title);
    }

    // Silencing a diagnostic is never the fix to land on by reflex - marking one preferred would
    // put it behind a single keystroke, ahead of the actual repair.
    [Fact]
    public void NoSuppressionIsMarkedPreferred()
    {
        Assert.DoesNotContain(Actions("aetswg-004-0001"), a => a.IsPreferred);
    }

    [Fact]
    public void LineScopedAction_InsertsTheDirectiveAboveTheDiagnostic()
    {
        var action = Actions("aetswg-004-0001").First(a => a.Title.Contains("this line"));
        var edit = action.Edit!.Changes![DocumentUri.From(Uri)].Single();

        Assert.Equal(2, edit.Range.Start.Line);
        Assert.Equal(0, edit.Range.Start.Character);
        // Indented to match the line it guards, so the result needs no reformatting.
        Assert.Equal("    <!-- aetswg:suppress aetswg-004-0001 -->\n", edit.NewText);
    }

    [Fact]
    public void ObjectScopedAction_InsertsInsideTheObject()
    {
        var action = Actions("aetswg-004-0001").First(a => a.Title.Contains("<Unit>"));
        var edit = action.Edit!.Changes![DocumentUri.From(Uri)].Single();

        Assert.Equal(2, edit.Range.Start.Line); // just inside <Unit> on line 1
        Assert.Contains("aetswg:suppress-object aetswg-004-0001", edit.NewText);
    }

    [Fact]
    public void FileScopedAction_InsertsJustInsideTheRootElement()
    {
        var action = Actions("aetswg-004-0001").First(a => a.Title.Contains("this file"));
        var edit = action.Edit!.Changes![DocumentUri.From(Uri)].Single();

        Assert.Equal(1, edit.Range.Start.Line);
        Assert.Contains("aetswg:suppress-file aetswg-004-0001", edit.NewText);
    }

    // Global suppression writes .aetswg/suppressions.json, so it has to go back to the server
    // rather than being a text edit the client applies.
    [Fact]
    public void ProjectScopedAction_IsACommandCarryingTheId()
    {
        var action = Actions("aetswg-004-0001").First(a => a.Title.Contains("across the project"));

        Assert.Null(action.Edit);
        Assert.Equal(SuppressionCommands.SuppressGlobally, action.Command!.Name);
        Assert.Equal("aetswg-004-0001", action.Command.Arguments!.Single().ToString());
    }

    // Outside any object there is no object scope to offer - three actions, not a bogus fourth.
    [Fact]
    public void DiagnosticOutsideAnyObject_OmitsTheObjectScope()
    {
        var titles = Actions("aetswg-004-0001", 0).Select(a => a.Title).ToList();

        Assert.Equal(3, titles.Count);
        Assert.DoesNotContain(titles, t => t.Contains("<Unit>"));
    }

    /// <summary>Schema where only &lt;Unit&gt; is an object type.</summary>
    private sealed class UnitObjectSchema : ISchemaProvider
    {
        public event EventHandler? SchemaRefreshed
        {
            add { }
            remove { }
        }

        public IReadOnlyList<XmlTagDefinition> AllTags => [];
        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
        public IReadOnlyList<EnumDefinition> AllEnums => [];
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
        public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

        public XmlTagDefinition? GetTag(string tagName)
        {
            return null;
        }

        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string tagName)
        {
            return [];
        }

        public GameObjectTypeDefinition? GetObjectType(string typeName)
        {
            return typeName.Equals("Unit", StringComparison.OrdinalIgnoreCase)
                ? new GameObjectTypeDefinition { TypeName = "Unit", NameTag = "Name" }
                : null;
        }

        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName)
        {
            return [];
        }

        public EnumDefinition? GetEnum(string enumName)
        {
            return null;
        }
    }
}

file sealed class FakeSuppressionWorkspaceHost : IGameWorkspaceHost
{
    private readonly string _text;
    private readonly string _uri;

    public FakeSuppressionWorkspaceHost(string uri, string text)
    {
        _uri = uri;
        _text = text;
    }

    public IEnumerable<TrackedDocument> All => [new(_uri, _text, 1)];

    public void AddOrUpdate(string uri, string text, int version, bool publishDiagnostics = true)
    {
    }

    public void Remove(string uri)
    {
    }

    public bool TryGet(string uri, out TrackedDocument doc)
    {
        doc = new TrackedDocument(_uri, _text, 1);
        return true;
    }
}
