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
///     When the producer reports that nothing reaches a file.
///     <para>
///         Measured in the engine: a file is read because a registry lists it, because the engine
///         opens it by a name compiled into the binary, or because it sits in a directory the engine
///         walks. The schema declares all three, so "no file type after registration, and not under
///         a scanned directory" is the whole of the rule.
///     </para>
/// </summary>
public sealed class UnregisteredXmlFileFactTest
{
    private const string Xml = "<?xml version=\"1.0\"?>\n<Orphan>\n  <Thing>1</Thing>\n</Orphan>";

    private static XmlDocumentFactProducer Build(
        ISchemaProvider? schema = null, IFileTypeRegistry? registry = null)
    {
        return new XmlDocumentFactProducer(
            new FileHelper(new MockFileSystem()),
            schema ?? new BareSchemaProvider(),
            registry ?? new BareFileTypeRegistry(),
            new XmlStructuralValidator());
    }

    [Fact]
    public void AFileNoRegistryNamesIsReported()
    {
        var facts = Build().Produce(Xml, "file:///data/xml/Orphan.xml");

        var fact = Assert.Single(facts.OfType<XmlUnregisteredFileFact>());
        Assert.Equal("Orphan.xml", fact.FileName);
    }

    /// <summary>
    ///     Anchored on the root element's NAME. Column 0 of line 0 would have put the squiggle on
    ///     the XML declaration, which is not the thing that is wrong.
    /// </summary>
    [Fact]
    public void ItIsAnchoredOnTheRootElementName()
    {
        var fact = Assert.Single(
            Build().Produce(Xml, "file:///data/xml/Orphan.xml").OfType<XmlUnregisteredFileFact>());

        var line = Xml.Split('\n')[fact.Line];
        Assert.Equal("Orphan", line.Substring(fact.Column, fact.Length));
    }

    /// <summary>
    ///     A metafile the schema declares but whose CONTENTS it does not model yet registers with an
    ///     empty type list. The engine opens such a file by a name compiled into it, so it is read -
    ///     and reporting it as unregistered says the opposite of the truth.
    /// </summary>
    /// <remarks>
    ///     <c>GetTypesForFile</c> returns the same empty array for "registered, no types" and "never
    ///     registered", so the question has to be asked of the registry's keys instead.
    ///     <c>guidialogs.xml</c> is the case that surfaced it.
    /// </remarks>
    [Fact]
    public void AFileRegisteredWithoutTypesIsSilent()
    {
        var registry = new BareFileTypeRegistry();
        registry.RegisterFile("file:///data/xml/Orphan.xml", []);

        var facts = Build(registry: registry).Produce(Xml, "file:///data/xml/Orphan.xml");

        Assert.Empty(facts.OfType<XmlUnregisteredFileFact>());
    }

    /// <summary>
    ///     A file we know is read but whose contents we have not modelled is not validated at all.
    ///     <para>
    ///         Without a file type every tag resolves through the global-tag fallback, so a name
    ///         that exists on some unrelated type wins: <c>&lt;Size&gt;</c> in
    ///         <c>guidialogs.xml</c> was being checked against a Float2 belonging to something else
    ///         entirely. Validating with the wrong rules is worse than not validating - the author
    ///         gets errors they cannot act on and learns to ignore the file.
    ///     </para>
    /// </summary>
    [Fact]
    public void AFileWhoseContentsAreNotModelledIsNotValidated()
    {
        var registry = new BareFileTypeRegistry();
        registry.RegisterFile("file:///data/xml/Guidialogs.xml", []);

        // The schema knows a global <Size> belonging to something else entirely - which is exactly
        // the situation that produced the false Float2 errors.
        var schema = new BareSchemaProvider
        {
            GlobalTag = new XmlTagDefinition { Tag = "Size", ValueType = XmlValueType.FloatVector2 }
        };

        var facts = Build(schema, registry)
            .Produce("<GUIDialogs>\n  <Dialog>\n    <Size>7</Size>\n  </Dialog>\n</GUIDialogs>",
                "file:///data/xml/Guidialogs.xml");

        Assert.Empty(facts.OfType<XmlTagValueFact>());
        Assert.Empty(facts.OfType<XmlUnregisteredFileFact>());
    }

    [Fact]
    public void AFileWithARegisteredTypeIsSilent()
    {
        var registry = new BareFileTypeRegistry();
        registry.RegisterFile("file:///data/xml/Orphan.xml", ["GameObjectType"]);

        var facts = Build(registry: registry).Produce(Xml, "file:///data/xml/Orphan.xml");

        Assert.Empty(facts.OfType<XmlUnregisteredFileFact>());
    }

    /// <summary>
    ///     A file under a directory the engine WALKS is read for sitting there, so no registry names
    ///     it and none was ever meant to. Asking why it is unregistered would ask the wrong question
    ///     of every AI goal set in the game.
    /// </summary>
    [Fact]
    public void AFileUnderAScannedDirectoryIsSilent()
    {
        ISchemaProvider schema = new BareSchemaProvider
        {
            ScannedDirectories = [new ScannedDirectoryDefinition("data/xml/ai/goals/", [])]
        };

        var facts = Build(schema).Produce(Xml, "file:///mod/Data/XML/AI/Goals/LandGoals.xml");

        Assert.Empty(facts.OfType<XmlUnregisteredFileFact>());
    }

    /// <summary>
    ///     The match is case-insensitive: the schema declares a game-relative path in lower case and
    ///     the document arrives however the file system spelled it.
    /// </summary>
    [Fact]
    public void AScannedDirectoryMatchesWhateverCaseTheDocumentUses()
    {
        ISchemaProvider schema = new BareSchemaProvider
        {
            ScannedDirectories = [new ScannedDirectoryDefinition("data/xml/enum/", [])]
        };

        Assert.True(schema.IsInScannedDirectory("file:///mod/DATA/XML/Enum/GameObjectCategoryType.xml"));
        Assert.False(schema.IsInScannedDirectory("file:///mod/Data/XML/Enumerations.xml"));
    }

    /// <summary>
    ///     A document with no element at all has nothing to anchor on, and a file that will not
    ///     parse has a louder problem than not being registered.
    /// </summary>
    [Fact]
    public void AnEmptyDocumentIsSilent()
    {
        Assert.Empty(Build().Produce("", "file:///data/xml/Orphan.xml").OfType<XmlUnregisteredFileFact>());
    }
}

file sealed class BareFileTypeRegistry : IFileTypeRegistry
{
    private readonly Dictionary<string, ImmutableArray<string>> _map = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, ImmutableArray<string>> All => _map;

    public ImmutableArray<string> GetTypesForFile(string fileUri)
    {
        return _map.TryGetValue(fileUri, out var types) ? types : [];
    }

    public void RegisterFile(string fileUri, ImmutableArray<string> typeNames)
    {
        _map[fileUri] = typeNames;
    }

    public void UnregisterFile(string fileUri)
    {
        _map.Remove(fileUri);
    }
}

file sealed class BareSchemaProvider : ISchemaProvider
{
    public IReadOnlyList<ScannedDirectoryDefinition> ScannedDirectories { get; init; } = [];

    public IReadOnlyList<ScannedDirectoryDefinition> AllScannedDirectories => ScannedDirectories;

    /// <summary>A tag the schema knows globally, standing in for a name shared across types.</summary>
    public XmlTagDefinition? GlobalTag { get; init; }

    public XmlTagDefinition? GetTag(string tagName)
    {
        return GlobalTag is not null && tagName.Equals(GlobalTag.Tag, StringComparison.OrdinalIgnoreCase)
            ? GlobalTag
            : null;
    }

    public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string tagName)
    {
        return [];
    }

    public GameObjectTypeDefinition? GetObjectType(string typeName)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName)
    {
        return [];
    }

    public EnumDefinition? GetEnum(string enumName)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> AllTags => [];
    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
    public IReadOnlyList<EnumDefinition> AllEnums => [];
    public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
    public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

    public event EventHandler? SchemaRefreshed
    {
        add { }
        remove { }
    }
}