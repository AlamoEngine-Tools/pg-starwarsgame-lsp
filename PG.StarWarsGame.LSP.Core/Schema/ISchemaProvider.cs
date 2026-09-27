// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Schema;

/// <summary>
///     Synchronous access to the in-memory tag registry loaded from the schema repository.
///     Loading and refresh are async; queries always hit an in-memory snapshot.
/// </summary>
public interface ISchemaProvider
{
    IReadOnlyList<XmlTagDefinition> AllTags { get; }
    IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes { get; }

    IReadOnlyList<EnumDefinition> AllEnums { get; }

    IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets { get; }

    IReadOnlyList<MetafileDefinition> AllMetafiles { get; }

    /// <summary>
    ///     Directories the engine walks, taking every file it finds. Default empty for the same
    ///     reason <see cref="AllKinds" /> is: a provider that predates them, and every test double,
    ///     still satisfies the interface - and a provider that reports none simply knows of no
    ///     directory whose files are exempt from registration.
    /// </summary>
    IReadOnlyList<ScannedDirectoryDefinition> AllScannedDirectories => [];

    /// <summary>
    ///     Whether this document sits under a directory the engine walks, in which case nothing
    ///     names it and nothing was ever supposed to.
    /// </summary>
    /// <param name="path">
    ///     A path or URI in any spelling; matched case-insensitively on the declared directory as a
    ///     substring, because the declaration is game-relative and the document is not.
    /// </param>
    bool IsInScannedDirectory(string path)
    {
        if (AllScannedDirectories.Count == 0) return false;

        var normalized = path.Replace('\\', '/');
        return AllScannedDirectories.Any(d =>
            normalized.Contains(d.Path, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     Object kinds from <c>kinds.yaml</c>: what makes an object a planet, a star base, a
    ///     squadron. Default empty so a provider that predates kinds, and every test double, still
    ///     satisfies the interface.
    /// </summary>
    IReadOnlyList<ObjectKindDefinition> AllKinds => [];

    /// <summary>The kind of that name, or null. Kind names are compared case-insensitively.</summary>
    ObjectKindDefinition? GetKind(string kindName)
    {
        return null;
    }

    /// <summary>
    ///     A task that completes when the provider has finished its initial load and is ready for queries.
    ///     Providers that load synchronously (e.g. <c>LocalFileSchemaProvider</c>) return
    ///     <see cref="Task.CompletedTask" /> so callers can await without blocking.
    ///     Providers that load asynchronously (e.g. <c>HttpSchemaProvider</c>) return a task that
    ///     completes after the first successful fetch.
    /// </summary>
    Task ReadyAsync => Task.CompletedTask;

    /// <summary>Returns the first definition found for this tag across all types. Use when context type is unknown.</summary>
    XmlTagDefinition? GetTag(string tagName);

    /// <summary>Returns every definition for this tag, one per KeyMapTable that declares it.</summary>
    IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string tagName);

    GameObjectTypeDefinition? GetObjectType(string typeName);

    /// <summary>Returns all tags defined for the given KeyMapTable type name.</summary>
    IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName);

    /// <summary>Returns the enum definition for the given C++ enum name, or null if unknown.</summary>
    EnumDefinition? GetEnum(string enumName);

    /// <summary>Fired when the schema index has been refreshed from the source.</summary>
    event EventHandler? SchemaRefreshed;
}