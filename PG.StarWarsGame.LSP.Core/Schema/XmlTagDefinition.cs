// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Schema;

public record XmlTagDefinition
{
    public required string Tag { get; init; }
    public required XmlValueType ValueType { get; init; }

    /// <summary>What this tag semantically references. <see cref="ReferenceKind.None" /> for non-reference types.</summary>
    public ReferenceKind ReferenceKind { get; init; }

    /// <summary>Non-null when ReferenceKind is XmlObject — the resolved target type (e.g. Faction, SFXEvent).</summary>
    public GameObjectTypeDefinition? ObjectType { get; init; }

    /// <summary>Non-null when ReferenceKind is HardcodedSet — the resolved hardcoded reference set.</summary>
    public HardcodedReferenceSet? HardcodedSet { get; init; }

    /// <summary>Non-null when ReferenceKind is Enum — the resolved enum definition.</summary>
    public EnumDefinition? Enum { get; init; }

    /// <summary>
    ///     Optional semantic refinement of the base <see cref="ValueType" />.
    ///     <see cref="TagSemanticType.Default" /> when no refinement is specified.
    /// </summary>
    public TagSemanticType SemanticType { get; init; }

    /// <summary>
    ///     Optional free-form key identifying which semantic subset of valid values is appropriate for this tag.
    ///     Used by completion providers to pre-filter suggestions.
    ///     <c>null</c> means "no restriction beyond what the base <see cref="ValueType" /> implies."
    /// </summary>
    public string? ValueGroup { get; init; }

    /// <summary>If true, this tag is deprecated and should not be used in new files.</summary>
    public bool Deprecated { get; init; }

    /// <summary>Game version in which this tag was introduced, e.g. "EaW 1.0" or "FoC 1.0". Null if unknown.</summary>
    public string? AvailableSince { get; init; }

    /// <summary>Locale → description text (e.g. "en" → "Max hit points…").</summary>
    public IReadOnlyDictionary<string, string> Description { get; init; } = new Dictionary<string, string>();

    /// <summary>Locale → secondary caveat text (e.g. "en" → "Deprecated; use Foo instead.").</summary>
    public IReadOnlyDictionary<string, string> Notes { get; init; } = new Dictionary<string, string>();

    /// <summary>If true, this tag may appear more than once under the same parent element; the engine merges all occurrences.</summary>
    public bool MultipleAllowed { get; init; }
}