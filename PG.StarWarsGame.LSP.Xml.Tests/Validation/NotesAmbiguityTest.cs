// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation;

/// <summary>
///     A tag's notes must not be shown on an owner that did not write them.
/// </summary>
/// <remarks>
///     <para>
///         Notes are collected by flat lookup, which returns whichever type happens to hold the name
///         - the same owner-agnostic answer that let an <c>allowedValues</c> narrowing leak. Notes
///         are documentation rather than a rule, so the fix is narrower than stripping them: a flat
///         note is trustworthy exactly when it cannot be anyone else's.
///     </para>
///     <para>
///         Latent rather than live at the time of writing: of 26 note-bearing tags, only
///         <c>Volume_Percent</c> is declared by two types (MusicEvent and SpeechEvent) and their
///         notes are byte-identical. It becomes visible the moment someone writes a per-owner note
///         on a shared name.
///     </para>
/// </remarks>
public sealed class NotesAmbiguityTest
{
    private const string Uri = "file:///test.xml";

    [Fact]
    public void A_note_from_the_only_declaring_type_is_shown()
    {
        var schema = new SharedTagSchema(("Solo", "the one note"));

        Assert.Single(Produce(schema, "<Root><Solo>1</Solo></Root>").OfType<XmlNotesFact>());
    }

    /// <summary>Two owners, one note between them - still unambiguous, so still shown.</summary>
    [Fact]
    public void Identical_notes_on_several_owners_are_still_shown()
    {
        var schema = new SharedTagSchema(("Shared", "same note"), ("Shared", "same note"));

        Assert.Single(Produce(schema, "<Root><Shared>1</Shared></Root>").OfType<XmlNotesFact>());
    }

    /// <summary>
    ///     Two owners that say different things. The flat lookup cannot say which applies here, and
    ///     showing one of them would be a guess presented as documentation.
    /// </summary>
    [Fact]
    public void Conflicting_notes_on_several_owners_are_not_shown()
    {
        var schema = new SharedTagSchema(("Shared", "one meaning"), ("Shared", "another meaning"));

        Assert.Empty(Produce(schema, "<Root><Shared>1</Shared></Root>").OfType<XmlNotesFact>());
    }

    /// <summary>A type that carries no note at all does not silence the one that does.</summary>
    [Fact]
    public void A_silent_owner_does_not_suppress_the_note()
    {
        var schema = new SharedTagSchema(("Shared", "the note"), ("Shared", null));

        Assert.Single(Produce(schema, "<Root><Shared>1</Shared></Root>").OfType<XmlNotesFact>());
    }

    private static IReadOnlyList<XmlFact> Produce(ISchemaProvider schema, string xml)
    {
        var producer = new XmlDocumentFactProducer(
            new FileHelper(new MockFileSystem()),
            schema,
            new EmptyFileTypeRegistry(),
            new XmlStructuralValidator(),
            []);

        return producer.Produce(xml, Uri);
    }

    /// <summary>One tag name, declared by one or more types, each with or without a note.</summary>
    private sealed class SharedTagSchema : ISchemaProvider
    {
        private readonly List<XmlTagDefinition> _definitions = [];

        public SharedTagSchema(params (string Tag, string? Note)[] declarations)
        {
            foreach (var (tag, note) in declarations)
                _definitions.Add(new XmlTagDefinition
                {
                    Tag = tag,
                    ValueType = XmlValueType.Float,
                    Notes = note is null
                        ? new Dictionary<string, string>()
                        : new Dictionary<string, string> { ["en"] = note }
                });
        }

        public XmlTagDefinition? GetTag(string tagName)
        {
            return _definitions.FirstOrDefault(d =>
                d.Tag.Equals(tagName, StringComparison.OrdinalIgnoreCase));
        }

        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string tagName)
        {
            return _definitions
                .Where(d => d.Tag.Equals(tagName, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName)
        {
            return [];
        }

        public IReadOnlyList<XmlTagDefinition> AllTags => _definitions;
        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
        public IReadOnlyList<EnumDefinition> AllEnums => [];
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
        public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

        public GameObjectTypeDefinition? GetObjectType(string typeName)
        {
            return null;
        }

        public EnumDefinition? GetEnum(string enumName)
        {
            return null;
        }

        public event EventHandler? SchemaRefreshed
        {
            add { }
            remove { }
        }
    }
}

file sealed class EmptyFileTypeRegistry : IFileTypeRegistry
{
    public IReadOnlyDictionary<string, ImmutableArray<string>> All =>
        new Dictionary<string, ImmutableArray<string>>();

    public ImmutableArray<string> GetTypesForFile(string _)
    {
        return ImmutableArray<string>.Empty;
    }

    public void RegisterFile(string fileUri, ImmutableArray<string> typeNames)
    {
    }

    public void UnregisterFile(string fileUri)
    {
    }
}