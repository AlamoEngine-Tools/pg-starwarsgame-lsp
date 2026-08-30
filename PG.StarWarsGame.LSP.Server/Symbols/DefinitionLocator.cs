// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Server.Symbols;

/// <summary>
///     Where a reference value is defined, or why it cannot be opened.
/// </summary>
/// <remarks>
///     The two failures are kept apart on purpose. "Nothing resolves" is the author's to fix; "it
///     resolves, but into the base game" is not, and a panel that reports the second as the first
///     sends the reader looking for a file that was never theirs.
/// </remarks>
public sealed record DefinitionLocation(
    string? Uri = null,
    int Line = 0,
    int Column = 0,
    string? Error = null);

/// <summary>Resolves a reference value to the workspace file that defines it.</summary>
public interface IDefinitionLocator
{
    /// <param name="value">The value as the XML writes it, e.g. an ability or planet name.</param>
    /// <param name="referenceType">
    ///     The type the reference is expected to be, when the schema declares one. Only a hint: an
    ///     unknown or umbrella name simply falls back to the untyped winner.
    /// </param>
    DefinitionLocation Locate(string? value, string? referenceType);
}

/// <summary>
///     The default locator, over the live game index.
/// </summary>
/// <remarks>
///     <para>
///         This was the body of the story editor's <c>aet/resolveStoryReference</c> handler, and it
///         was never story-specific - it is "given a name and the type it should be, where is that
///         written". Sitting inside that handler it was reachable only while the story editor
///         feature flag was on, which is no basis for the preview's ability rows to be able to jump
///         to a <c>SpecialAbility</c> definition. The handler now maps this, and a second,
///         ungated handler exposes it to everyone else.
///     </para>
/// </remarks>
public sealed class DefinitionLocator(IGameIndexService indexService, ISchemaProvider schema)
    : IDefinitionLocator
{
    /// <inheritdoc />
    public DefinitionLocation Locate(string? value, string? referenceType)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new DefinitionLocation(Error: "Nothing to resolve.");

        var trimmed = value.Trim();
        var symbol = Resolve(indexService.Current, trimmed, referenceType);
        if (symbol is null)
            return new DefinitionLocation(
                Error: $"'{trimmed}' does not resolve to any known definition.");

        if (symbol.Origin is not FileOrigin origin)
            return new DefinitionLocation(
                Error: $"'{trimmed}' is defined in the base game or an archive - " +
                       "there is no workspace file to open.");

        return new DefinitionLocation(origin.Uri, origin.Line, origin.Column ?? 0);
    }

    private GameSymbol? Resolve(GameIndex index, string value, string? referenceType)
    {
        // Prefer a type-matched definition when the referenceType names a concrete symbol type;
        // umbrella names (GameObjectType) and unknown types fall back to the untyped winner.
        var preferred = referenceType switch
        {
            null => null,
            StoryReferenceTypes.EventName => StoryReferenceTypes.EventSymbol,
            StoryReferenceTypes.Notification => StoryReferenceTypes.NotificationSymbol,
            StoryReferenceTypes.Flag => StoryReferenceTypes.FlagSymbol,
            _ when string.Equals(referenceType, "GameObjectType", StringComparison.OrdinalIgnoreCase) => null,
            _ when schema.GetObjectType(referenceType) is not null => referenceType,
            _ => null
        };

        var symbol = preferred is not null ? index.Resolve(value, preferred) : index.Resolve(value);
        if (symbol is not null) return symbol;

        // Scoped ability IDs are indexed as "OWNER$name" while references carry the bare name.
        return index.WorkspaceDefinitions.Keys
            .Where(k => k.IndexOf('$') is var i and >= 0 &&
                        string.Equals(k[(i + 1)..], value, StringComparison.OrdinalIgnoreCase))
            .Select(k => index.Resolve(k))
            .FirstOrDefault(s => s is not null);
    }
}
