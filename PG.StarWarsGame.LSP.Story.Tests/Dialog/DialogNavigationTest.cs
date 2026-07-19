// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Story.Dialog;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Story.Tests.Dialog;

/// <summary>Inlay hints and go-to-definition over story-dialog scripts.</summary>
public sealed class DialogNavigationTest
{
    private const string Uri = "file:///ws/dialogs/dialog_test.txt";
    private const string SpeechXmlUri = "file:///ws/data/xml/speechevents.xml";

    // ── Fixture ──────────────────────────────────────────────────────────────

    private static readonly ISchemaProvider Schema = new StubSchemaProvider(new EnumDefinition
    {
        Name = "StoryDialogCommand",
        Values =
        [
            new EnumValueDefinition
            {
                Name = "TEXT",
                Params =
                [
                    new ParamDefinition
                    {
                        Position = 0, ValueType = XmlValueType.NameReference,
                        ReferenceKind = ReferenceKind.LocalisationKey
                    }
                ]
            },
            new EnumValueDefinition
            {
                Name = "DIALOG",
                Params =
                [
                    new ParamDefinition
                    {
                        Position = 0, ValueType = XmlValueType.NameReference,
                        ReferenceKind = ReferenceKind.XmlObject,
                        ObjectType = new GameObjectTypeDefinition { TypeName = "SpeechEvent" }
                    }
                ]
            }
        ]
    });

    private static GameIndex Index()
    {
        var speech = new GameSymbol("Speech_Intro", GameSymbolKind.XmlObject, "SpeechEvent",
            new FileOrigin(SpeechXmlUri, 7, 20), null);
        return GameIndex.Empty with
        {
            WorkspaceDefinitions = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
                .WithComparers(StringComparer.OrdinalIgnoreCase)
                .Add("Speech_Intro", [speech]),
            Localisation = new StubLocalisation(new Dictionary<string, string>
            {
                ["TEXT_INTRO"] = "The battle begins."
            })
        };
    }

    private static ILspConfigurationProvider Config(bool inlayHints = true, bool goToDefinition = true)
    {
        return FakeLspConfigurationProvider.WithFeatures(new FeatureFlags
        {
            Dialog = new DialogFeatureFlags { InlayHints = inlayHints, GoToDefinition = goToDefinition }
        });
    }

    private static DialogInlayHintHandler HintHandler(
        string text, bool inScope = true, bool flag = true)
    {
        return new DialogInlayHintHandler(
            new StubIndexService(Index()), new StubScope(inScope), new DialogFactProducer(Schema),
            new FileHelper(new MockFileSystem()),
            new StubTextSource(Uri, text), Config(flag));
    }

    private static DialogDefinitionHandler DefinitionHandler(
        string text, bool inScope = true, bool flag = true)
    {
        return new DialogDefinitionHandler(
            new StubIndexService(Index()), new StubScope(inScope), new DialogFactProducer(Schema),
            new FileHelper(new MockFileSystem()),
            new StubTextSource(Uri, text), NullLogger<DialogDefinitionHandler>.Instance,
            Config(goToDefinition: flag));
    }

    private static InlayHintParams Hints(int endLine = 99)
    {
        return new InlayHintParams
        {
            TextDocument = new TextDocumentIdentifier(Uri),
            Range = new Range(0, 0, endLine, 0)
        };
    }

    private static DefinitionParams At(int line, int character)
    {
        return new DefinitionParams
        {
            TextDocument = new TextDocumentIdentifier(Uri),
            Position = new Position(line, character)
        };
    }

    // ── Inlay hints ──────────────────────────────────────────────────────────

    [Fact]
    public async Task InlayHints_KnownKey_ShowsTranslatedTextAtEndOfLine()
    {
        var handler = HintHandler("[CHAPTER 0]\nTEXT TEXT_INTRO");

        var hints = await handler.Handle(Hints(), CancellationToken.None);

        var hint = Assert.Single(hints!);
        Assert.Equal("\"The battle begins.\"", hint.Label.String);
        Assert.Equal(1, hint.Position!.Line);
    }

    [Fact]
    public async Task InlayHints_UnknownKey_ShowsMissingSuffix()
    {
        var handler = HintHandler("[CHAPTER 0]\nTEXT TEXT_GHOST");

        var hints = await handler.Handle(Hints(), CancellationToken.None);

        Assert.Equal("\"TEXT_GHOST: MISSING\"", Assert.Single(hints!).Label.String);
    }

    [Fact]
    public async Task InlayHints_LineOutsideRequestedRange_IsSkipped()
    {
        var handler = HintHandler("[CHAPTER 0]\nTEXT TEXT_INTRO");

        var hints = await handler.Handle(Hints(0), CancellationToken.None);

        Assert.Empty(hints!);
    }

    [Fact]
    public async Task InlayHints_OutOfScopeDocument_ReturnsNull()
    {
        var handler = HintHandler("[CHAPTER 0]\nTEXT TEXT_INTRO", false);

        Assert.Null(await handler.Handle(Hints(), CancellationToken.None));
    }

    [Fact]
    public async Task InlayHints_FlagOff_ReturnsNull()
    {
        var handler = HintHandler("[CHAPTER 0]\nTEXT TEXT_INTRO", flag: false);

        Assert.Null(await handler.Handle(Hints(), CancellationToken.None));
    }

    // ── Go-to-definition ─────────────────────────────────────────────────────

    [Fact]
    public async Task Definition_OnSpeechEventArgument_JumpsToDefiningXml()
    {
        var handler = DefinitionHandler("[CHAPTER 0]\nDIALOG Speech_Intro");

        // Position inside "Speech_Intro" (line 1, arg starts at column 7).
        var result = await handler.Handle(At(1, 10), CancellationToken.None);

        var location = Assert.Single(result!).Location!;
        Assert.Equal(SpeechXmlUri, location.Uri.ToString());
        Assert.Equal(7, location.Range.Start.Line);
    }

    [Fact]
    public async Task Definition_OnLocalisationKeyArgument_ReturnsNull()
    {
        // Localisation keys have no recorded file/line - deliberately not navigable.
        var handler = DefinitionHandler("[CHAPTER 0]\nTEXT TEXT_INTRO");

        Assert.Null(await handler.Handle(At(1, 8), CancellationToken.None));
    }

    [Fact]
    public async Task Definition_PositionOutsideAnyArgument_ReturnsNull()
    {
        var handler = DefinitionHandler("[CHAPTER 0]\nDIALOG Speech_Intro");

        Assert.Null(await handler.Handle(At(1, 2), CancellationToken.None));
    }

    [Fact]
    public async Task Definition_UnresolvableReference_ReturnsNull()
    {
        var handler = DefinitionHandler("[CHAPTER 0]\nDIALOG Speech_Ghost");

        Assert.Null(await handler.Handle(At(1, 10), CancellationToken.None));
    }

    [Fact]
    public async Task Definition_OutOfScopeDocument_ReturnsNull()
    {
        var handler = DefinitionHandler("[CHAPTER 0]\nDIALOG Speech_Intro", false);

        Assert.Null(await handler.Handle(At(1, 10), CancellationToken.None));
    }

    [Fact]
    public async Task Definition_FlagOff_ReturnsNull()
    {
        var handler = DefinitionHandler("[CHAPTER 0]\nDIALOG Speech_Intro", flag: false);

        Assert.Null(await handler.Handle(At(1, 10), CancellationToken.None));
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class StubScope(bool inScope) : IStoryDialogScope
    {
        public bool Enabled => inScope;

        public bool IsInScope(string canonicalUri)
        {
            return inScope;
        }

        public string? ResolveDialogFile(string dialogName)
        {
            return null;
        }

        public IReadOnlyCollection<int> GetChapters(string canonicalUri)
        {
            return [];
        }
    }

    private sealed class StubTextSource(string uri, string text) : IDocumentTextSource
    {
        public DocumentText? GetText(string canonicalUri)
        {
            return string.Equals(canonicalUri, uri, StringComparison.OrdinalIgnoreCase)
                ? new DocumentText(text, 0, true)
                : null;
        }
    }

    private sealed class StubLocalisation(IReadOnlyDictionary<string, string> entries) : ILocalisationIndex
    {
        public IEnumerable<string> Keys => entries.Keys;

        public bool ContainsKey(string key)
        {
            return entries.ContainsKey(key);
        }

        public string? GetValue(string key)
        {
            return entries.GetValueOrDefault(key);
        }
    }

    private sealed class StubSchemaProvider(EnumDefinition dialogCommands) : ISchemaProvider
    {
        public event EventHandler? SchemaRefreshed
        {
            add { }
            remove { }
        }

        public IReadOnlyList<XmlTagDefinition> AllTags => [];
        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
        public IReadOnlyList<EnumDefinition> AllEnums => [dialogCommands];
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

        public EnumDefinition? GetEnum(string name)
        {
            return string.Equals(name, dialogCommands.Name, StringComparison.OrdinalIgnoreCase)
                ? dialogCommands
                : null;
        }

        public GameObjectTypeDefinition? GetObjectType(string t)
        {
            return null;
        }
    }

    private sealed class StubIndexService(GameIndex index) : IGameIndexService
    {
        public GameIndex Current => index;

        public event Action<GameIndex>? IndexChanged
        {
            add { }
            remove { }
        }

        public event Action<ILocalisationIndex>? LocalisationChanged
        {
            add { }
            remove { }
        }

        public event Action<GameIndex>? DynamicEnumChanged
        {
            add { }
            remove { }
        }

        public Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task OpenDocumentAsync(string uri, string text, int version, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public void InjectDocument(DocumentIndex document)
        {
        }

        public void RemoveDocument(string uri)
        {
        }

        public void ApplyBaseline(BaselineIndex baseline)
        {
        }

        public void ApplyLocalisation(ILocalisationIndex index)
        {
        }

        public void ApplyAssetFiles(IAssetFileIndex index)
        {
        }

        public void ApplyModelBones(ImmutableDictionary<string, ImmutableArray<string>> bones)
        {
        }

        public void ApplyWorkspaceDynamicEnumValues(ImmutableDictionary<string, ImmutableArray<string>> values)
        {
        }

        public void ApplyWorkspaceEnumValueDefinitions(
            ImmutableDictionary<string, ImmutableDictionary<string, FileOrigin>> definitions)
        {
        }

        public IDisposable BeginBulkUpdate()
        {
            return new Noop();
        }

        private sealed class Noop : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}