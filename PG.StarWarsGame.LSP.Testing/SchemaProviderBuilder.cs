// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Testing;

/// <summary>
///     Builds the in-memory schema a test runs against (#304). A test names the tags, types, enums
///     and sets it needs and nothing else; the lookups are derived from those lists the way the real
///     index derives them from the schema files.
/// </summary>
/// <remarks>
///     Keyed as the real index keys: names compare case-insensitively, <c>GetTag</c> answers the
///     FIRST declaration of a name, <c>GetAllTagDefinitions</c> every one, and <c>GetTagsForType</c>
///     the tags declared on that type through <see cref="WithTags" />. A tag added with
///     <see cref="WithTag" /> belongs to no type. The built <see cref="FakeSchemaProvider" /> takes
///     further entries through its <c>Add</c> methods, for a test that registers after wiring.
/// </remarks>
public sealed class SchemaProviderBuilder
{
    private readonly List<Action<FakeSchemaProvider>> _steps = [];
    private Task _ready = Task.CompletedTask;

    /// <summary>A tag declared on no particular type: found by name, listed for no type.</summary>
    public SchemaProviderBuilder WithTag(XmlTagDefinition tag)
    {
        _steps.Add(p => p.Add(tag));
        return this;
    }

    /// <summary>Tags declared on one type, in this order: found by name and listed for the type.</summary>
    public SchemaProviderBuilder WithTags(string typeName, params XmlTagDefinition[] tags)
    {
        _steps.Add(p => p.AddForType(typeName, tags));
        return this;
    }

    public SchemaProviderBuilder WithObjectType(GameObjectTypeDefinition type)
    {
        _steps.Add(p => p.Add(type));
        return this;
    }

    public SchemaProviderBuilder WithEnum(EnumDefinition definition)
    {
        _steps.Add(p => p.Add(definition));
        return this;
    }

    public SchemaProviderBuilder WithEnums(params EnumDefinition[] definitions)
    {
        foreach (var definition in definitions)
            WithEnum(definition);
        return this;
    }

    public SchemaProviderBuilder WithHardcodedSet(HardcodedReferenceSet set)
    {
        _steps.Add(p => p.Add(set));
        return this;
    }

    public SchemaProviderBuilder WithMetafile(MetafileDefinition metafile)
    {
        _steps.Add(p => p.Add(metafile));
        return this;
    }

    public SchemaProviderBuilder WithMetafiles(params MetafileDefinition[] metafiles)
    {
        foreach (var metafile in metafiles)
            WithMetafile(metafile);
        return this;
    }

    public SchemaProviderBuilder WithScannedDirectory(ScannedDirectoryDefinition directory)
    {
        _steps.Add(p => p.Add(directory));
        return this;
    }

    public SchemaProviderBuilder WithKind(ObjectKindDefinition kind)
    {
        _steps.Add(p => p.Add(kind));
        return this;
    }

    /// <summary>
    ///     The task <c>ReadyAsync</c> hands out, for a test of something that waits on the schema.
    ///     Completed unless set.
    /// </summary>
    public SchemaProviderBuilder WithReady(Task ready)
    {
        _ready = ready;
        return this;
    }

    public FakeSchemaProvider Build()
    {
        var provider = new FakeSchemaProvider(_ready);
        foreach (var step in _steps)
            step(provider);
        return provider;
    }
}