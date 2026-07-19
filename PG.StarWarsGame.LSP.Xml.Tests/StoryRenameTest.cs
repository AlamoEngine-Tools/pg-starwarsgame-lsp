// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Tests.Fakes;

namespace PG.StarWarsGame.LSP.Xml.Tests;

public sealed class StoryRenameTest
{
    private const string ThreadA = "file:///ws/data/xml/story_a.xml";
    private const string ThreadB = "file:///ws/data/xml/story_b.xml";
    private const string LuaScript = "file:///ws/data/scripts/story/story_a.lua";

    // <Event Name="Mission_Start"> in thread A at line 1; the column is the value start.
    private const int DefLine = 1;
    private const int DefColumn = 13;

    private static GameIndex StoryIndex(string typeName = "StoryEvent", int definitionCount = 1)
    {
        var defs = ImmutableArray.CreateBuilder<GameSymbol>();
        for (var i = 0; i < definitionCount; i++)
            defs.Add(new GameSymbol("Mission_Start", GameSymbolKind.XmlObject, typeName,
                new FileOrigin(i == 0 ? ThreadA : ThreadB, DefLine, DefColumn), null));

        var prereqRef = new GameReference("Mission_Start", GameSymbolKind.XmlObject, typeName,
            ThreadB, 3, 8, "Mission_Start".Length);
        var luaKeyRef = new GameReference("Mission_Start", GameSymbolKind.XmlObject, typeName,
            LuaScript, 2, 4, "Mission_Start".Length);

        var docA = new DocumentIndex(ThreadA, 1, [defs[0]], []);
        return GameIndex.Empty with
        {
            Documents = ImmutableDictionary<string, DocumentIndex>.Empty.Add(ThreadA, docA),
            WorkspaceDefinitions = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
                .Add("Mission_Start", defs.ToImmutable()),
            WorkspaceReferences = ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty
                .Add("Mission_Start", [prereqRef, luaKeyRef])
        };
    }

    private static XmlRenameHandler Handler(FeatureFlags? features = null)
    {
        return new XmlRenameHandler(new AllowAllEaWContext(),
            new NullTextSource(), new EmptySchema(), NullLogger<XmlRenameHandler>.Instance,
            features is null ? null : FakeLspConfigurationProvider.WithFeatures(features));
    }

    private static RenameParams Rename(string newName)
    {
        return new RenameParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = ThreadA },
            Position = new Position(DefLine, DefColumn + 2),
            NewName = newName
        };
    }

    [Fact]
    public void StoryEventRename_EditsDefinitionAndEveryReference_AcrossXmlAndLua()
    {
        var edit = Handler().HandleRename(ThreadA, Rename("Mission_Begin"), StoryIndex());

        Assert.NotNull(edit);
        var changes = edit!.Changes!.ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value.ToList());
        Assert.Equal(3, changes.Count);

        var definitionEdit = Assert.Single(changes[ThreadA]);
        Assert.Equal(DefColumn, definitionEdit.Range.Start.Character);
        Assert.Equal(DefColumn + "Mission_Start".Length, definitionEdit.Range.End.Character);
        Assert.Equal("Mission_Begin", definitionEdit.NewText);

        Assert.Equal(8, Assert.Single(changes[ThreadB]).Range.Start.Character);
        Assert.Equal(4, Assert.Single(changes[LuaScript]).Range.Start.Character);
    }

    [Fact]
    public void AmbiguousStoryEventName_RenameIsRejectedWithMessage()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            Handler().HandleRename(ThreadA, Rename("Anything"), StoryIndex(definitionCount: 2)));

        Assert.Contains("2 story events", exception.Message);
    }

    [Fact]
    public void AmbiguousStoryEventName_PrepareIsRejectedWithMessage()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            Handler().HandlePrepare(ThreadA, DefLine, DefColumn + 2, StoryIndex(definitionCount: 2)));

        Assert.Contains("2 story events", exception.Message);
    }

    [Fact]
    public void FlagRename_OverlongNewName_IsRejectedWithMessage()
    {
        var longName = new string('F', 32);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Handler().HandleRename(ThreadA, Rename(longName), StoryIndex("StoryFlag")));

        Assert.Contains("31", exception.Message);
    }

    [Fact]
    public void FlagRename_WithinLimit_Succeeds()
    {
        var edit = Handler().HandleRename(ThreadA, Rename("NEW_FLAG"), StoryIndex("StoryFlag"));

        Assert.NotNull(edit);
    }

    [Fact]
    public void StoryRenameFlagOff_ReturnsNullForBothRenameAndPrepare()
    {
        var features = new FeatureFlags { Story = new StoryFeatureFlags { Rename = false } };

        Assert.Null(Handler(features).HandleRename(ThreadA, Rename("X"), StoryIndex()));
        Assert.Null(Handler(features).HandlePrepare(ThreadA, DefLine, DefColumn + 2, StoryIndex()));
    }

    private sealed class NullTextSource : IDocumentTextSource
    {
        public DocumentText? GetText(string canonicalUri)
        {
            return null;
        }
    }

    private sealed class EmptySchema : ISchemaProvider
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

        public XmlTagDefinition? GetTag(string t)
        {
            return null;
        }

        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string t)
        {
            return [];
        }

        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string t)
        {
            return [];
        }

        public EnumDefinition? GetEnum(string e)
        {
            return null;
        }

        public GameObjectTypeDefinition? GetObjectType(string t)
        {
            return null;
        }
    }
}