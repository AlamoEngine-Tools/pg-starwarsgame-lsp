namespace PG.StarWarsGame.LSP.Core.Schema;

public record XmlTagDefinition
{
    public required string Tag { get; init; }
    public required XmlValueType ValueType { get; init; }

    /// <summary>What this tag semantically references. <see cref="ReferenceKind.None" /> for non-reference types.</summary>
    public ReferenceKind ReferenceKind { get; init; }

    /// <summary>Non-null when ReferenceKind is XmlObject — identifies the specific target type (e.g. "Faction", "SFXEvent").</summary>
    public string? ReferenceType { get; init; }

    /// <summary>Non-null when ReferenceKind is Enum — names the enum definition to validate against.</summary>
    public string? EnumName { get; init; }

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

    /// <summary>If true, this tag may appear more than once under the same parent element; the engine merges all occurrences.</summary>
    public bool MultipleAllowed { get; init; }
}