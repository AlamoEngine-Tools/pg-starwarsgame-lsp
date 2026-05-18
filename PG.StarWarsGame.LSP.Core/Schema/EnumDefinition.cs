namespace PG.StarWarsGame.LSP.Core.Schema;

public sealed record EnumValueDefinition
{
    public required string Name { get; init; }
    public IReadOnlyDictionary<string, string> Description { get; init; } = new Dictionary<string, string>();
    public bool Deprecated { get; init; }
    public string? AvailableSince { get; init; }

    /// <summary>
    ///     Zero or more value-group keys this value belongs to. Empty means "all groups."
    ///     Used by completion providers to pre-filter suggestions when the enclosing tag
    ///     carries a <c>valueGroup</c> annotation.
    /// </summary>
    public IReadOnlyList<string> Groups { get; init; } = [];
}

public sealed record EnumDefinition
{
    public required string Name { get; init; }
    public EnumKind Kind { get; init; }
    public bool IsBitfield { get; init; }

    /// <summary>
    ///     Archive-root-relative path to the XML file that defines this enum at runtime (e.g.
    ///     "data/xml/enum/gameobjectcategorytype.xml").
    ///     Non-null only when <see cref="Kind" /> is <see cref="EnumKind.DynamicXml" />.
    /// </summary>
    public string? SourceFile { get; init; }

    public IReadOnlyDictionary<string, string> Description { get; init; } = new Dictionary<string, string>();
    public bool Deprecated { get; init; }
    public string? AvailableSince { get; init; }
    public required IReadOnlyList<EnumValueDefinition> Values { get; init; }
}