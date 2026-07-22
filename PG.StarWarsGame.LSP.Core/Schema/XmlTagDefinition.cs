// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Schema;

public record XmlTagDefinition
{
    public required string Tag { get; init; }
    public required XmlValueType ValueType { get; init; }

    /// <summary>What this tag semantically references. <see cref="ReferenceKind.None" /> for non-reference types.</summary>
    public ReferenceKind ReferenceKind { get; init; }

    /// <summary>Non-null when ReferenceKind is XmlObject - the resolved target type (e.g. Faction, SFXEvent).</summary>
    public GameObjectTypeDefinition? ObjectType { get; init; }

    /// <summary>
    ///     The raw <c>referenceType</c> string from the schema. For <see cref="ReferenceKind.WorkspaceFile" />
    ///     this names the target file-type (e.g. <c>StoryPlotManifest</c>, <c>StoryParser</c>,
    ///     <c>LuaScript</c>) used to build the <see cref="WorkspaceFileKey" />. Null when unset.
    /// </summary>
    public string? ReferenceTypeName { get; init; }

    /// <summary>Non-null when ReferenceKind is HardcodedSet - the resolved hardcoded reference set.</summary>
    public HardcodedReferenceSet? HardcodedSet { get; init; }

    /// <summary>Non-null when ReferenceKind is Enum - the resolved enum definition.</summary>
    public EnumDefinition? Enum { get; init; }

    /// <summary>
    ///     Optional semantic refinement of the base <see cref="ValueType" />.
    ///     <see cref="TagSemanticType.Default" /> when no refinement is specified.
    /// </summary>
    public TagSemanticType SemanticType { get; init; }

    /// <summary>
    ///     Ordered list of group keys restricting which hardcoded-set or enum values are valid for this tag.
    ///     Empty means "no restriction." When non-empty, completion providers rank values by their position
    ///     in this list (first group = highest priority) and diagnostics reject values that belong to none
    ///     of the listed groups (values with empty <see cref="HardcodedReferenceSetValue.Groups" /> are always accepted).
    /// </summary>
    public IReadOnlyList<string> ValueGroups { get; init; } = [];

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

    /// <summary>
    ///     How this tag composes with the same tag on a base object when the enclosing GameObject is a
    ///     variant (declares <c>Variant_Of_Existing_Type</c>). Defaults to <see cref="VariantMode.Replace" />.
    /// </summary>
    public VariantMode VariantMode { get; init; }

    /// <summary>
    ///     Optional override that controls how custom named validation handlers compose with the default type handler.
    ///     <c>null</c> means "run all registered handlers for this fact type" (default behaviour).
    /// </summary>
    public TagValidationOverride? ValidationOverride { get; init; }
}